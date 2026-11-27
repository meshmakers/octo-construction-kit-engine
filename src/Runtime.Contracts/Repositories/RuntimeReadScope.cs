using System.Collections.Concurrent;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
///     Names the caller of the runtime-entity reads that run inside it, so a repository can tell an operator who read
///     without a bound (a pipeline, its execution and the node) without a parameter on every read method. The ETL
///     runtime opens an execution scope per pipeline execution and a node scope around every node:
///     <code>
///     using (RuntimeReadScope.BeginExecution(new RuntimeReadExecution(tenantId, pipelineId, null, dataFlowId, executionId)))
///     {
///         using (RuntimeReadScope.BeginNode("GetRtEntitiesByType@1", "GetRtEntitiesByType@1[0]"))
///         {
///             await node.ProcessObjectAsync(...);
///         }
///     }
///     </code>
///     The scope holds for the code that runs inside it, across <c>await</c> and for the tasks it starts (it flows with
///     the execution context, <see cref="AsyncLocal{T}" />), and ends when it is disposed: the caller that was current
///     before applies again. Scopes nest; the innermost wins, and a node scope keeps the execution of the scope around
///     it. Reads outside every scope have no caller (<see cref="Current" /> is null).
///     <para>
///         The scope changes no read: it only describes the caller. Repositories use it for observability (one warning
///         per execution and node, <see cref="RuntimeReadExecution.TryMarkReported" /> with <see cref="RuntimeReadCaller.NodeKey" />).
///     </para>
/// </summary>
public static class RuntimeReadScope
{
    private static readonly AsyncLocal<RuntimeReadCaller?> CurrentCaller = new();

    /// <summary>
    ///     The caller of the innermost scope, or null when no scope is active.
    /// </summary>
    public static RuntimeReadCaller? Current => CurrentCaller.Value;

    /// <summary>
    ///     Opens the scope of one execution (e.g. one pipeline execution). Node scopes opened inside it share the
    ///     execution and its record of reported nodes.
    /// </summary>
    /// <param name="execution">The execution; create one per execution.</param>
    /// <returns>The scope; dispose it to restore the previous caller.</returns>
    public static IDisposable BeginExecution(RuntimeReadExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);
        return new Scope(new RuntimeReadCaller(execution, null, null, false));
    }

    /// <summary>
    ///     Opens the scope of one node (or another named step) inside the current execution scope. Without an
    ///     execution scope the node has no execution (<see cref="RuntimeReadCaller.Execution" /> is null).
    /// </summary>
    /// <param name="nodeType">The type of the node, e.g. its qualified name <c>GetRtEntitiesByType@1</c>.</param>
    /// <param name="nodePath">The path of the node in the pipeline, as operators read it.</param>
    /// <param name="nodeKey">
    ///     What identifies the node within the execution, when the path does not (e.g. two nodes of the same type in
    ///     one sequence); runs of the same node (loop iterations) share it. Default: <paramref name="nodePath" />.
    /// </param>
    /// <returns>The scope; dispose it to restore the previous caller.</returns>
    public static IDisposable BeginNode(string nodeType, string nodePath, string? nodeKey = null)
    {
        var current = CurrentCaller.Value;
        return new Scope(new RuntimeReadCaller(current?.Execution, nodeType, nodePath, current?.Suppressed ?? false)
        {
            NodeKey = nodeKey ?? nodePath
        });
    }

    /// <summary>
    ///     Marks the reads of the scope as deliberate (internal batch work such as migrations, exports or imports
    ///     that read whole types on purpose): repositories do not report them as unbounded.
    /// </summary>
    /// <returns>The scope; dispose it to restore the previous caller.</returns>
    public static IDisposable Suppress()
    {
        var current = CurrentCaller.Value;
        return new Scope(new RuntimeReadCaller(current?.Execution, current?.NodeType, current?.NodePath, true)
        {
            NodeKey = current?.NodeKey
        });
    }

    private sealed class Scope : IDisposable
    {
        private readonly RuntimeReadCaller? _previous;
        private bool _disposed;

        public Scope(RuntimeReadCaller caller)
        {
            _previous = CurrentCaller.Value;
            CurrentCaller.Value = caller;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CurrentCaller.Value = _previous;
        }
    }
}

/// <summary>
///     The caller of a runtime-entity read, set by <see cref="RuntimeReadScope" />.
/// </summary>
/// <param name="Execution">The execution the read belongs to, or null outside an execution scope.</param>
/// <param name="NodeType">The type of the node that reads, or null outside a node scope.</param>
/// <param name="NodePath">The path of the node that reads, or null outside a node scope.</param>
/// <param name="Suppressed">True inside <see cref="RuntimeReadScope.Suppress" />: the reads are deliberate.</param>
public sealed record RuntimeReadCaller(
    RuntimeReadExecution? Execution,
    string? NodeType,
    string? NodePath,
    bool Suppressed)
{
    /// <summary>
    ///     What identifies the node within the execution (see <see cref="RuntimeReadScope.BeginNode" />), or null outside
    ///     a node scope. Pass it to <see cref="RuntimeReadExecution.TryMarkReported" />.
    /// </summary>
    public string? NodeKey { get; init; }
}

/// <summary>
///     One execution (e.g. one pipeline execution) whose reads <see cref="RuntimeReadScope" /> describes: who runs it,
///     and which nodes were already reported, so a repository reports each node once per execution however often the
///     node runs (e.g. inside a loop).
/// </summary>
public sealed class RuntimeReadExecution
{
    private readonly ConcurrentDictionary<string, byte> _reportedNodes = new(StringComparer.Ordinal);

    /// <summary>
    ///     Creates the description of one execution.
    /// </summary>
    /// <param name="tenantId">The tenant the execution runs for.</param>
    /// <param name="pipelineId">The id of the pipeline, if any.</param>
    /// <param name="pipelineName">The name of the pipeline, if known.</param>
    /// <param name="dataFlowId">The id of the data flow, if any.</param>
    /// <param name="executionId">The id of this execution, if any.</param>
    public RuntimeReadExecution(string? tenantId, string? pipelineId, string? pipelineName, string? dataFlowId,
        string? executionId)
    {
        TenantId = tenantId;
        PipelineId = pipelineId;
        PipelineName = pipelineName;
        DataFlowId = dataFlowId;
        ExecutionId = executionId;
    }

    /// <summary>The tenant the execution runs for.</summary>
    public string? TenantId { get; }

    /// <summary>The id of the pipeline, if any.</summary>
    public string? PipelineId { get; }

    /// <summary>The name of the pipeline, if known.</summary>
    public string? PipelineName { get; }

    /// <summary>The id of the data flow, if any.</summary>
    public string? DataFlowId { get; }

    /// <summary>The id of this execution, if any.</summary>
    public string? ExecutionId { get; }

    /// <summary>
    ///     Records that the node <paramref name="nodeKey" /> was reported in this execution. Thread-safe.
    /// </summary>
    /// <param name="nodeKey">
    ///     The node key (<see cref="RuntimeReadCaller.NodeKey" />); null stands for reads outside a node.
    /// </param>
    /// <returns>True the first time for a node key, false afterwards.</returns>
    public bool TryMarkReported(string? nodeKey) => _reportedNodes.TryAdd(nodeKey ?? string.Empty, 0);
}

using Meshmakers.Octo.Runtime.Contracts.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     The ambient caller of runtime-entity reads: execution and node scopes nest, flow across await and into started
///     tasks, restore the previous caller on dispose, and an execution reports each node path once.
/// </summary>
public class RuntimeReadScopeTests
{
    private static RuntimeReadExecution NewExecution() =>
        new("tenant-a", "pipeline-1", "Import meters", "flow-1", "exec-1");

    [Fact]
    public void Current_WithoutScope_IsNull()
    {
        Assert.Null(RuntimeReadScope.Current);
    }

    [Fact]
    public void BeginNode_InsideExecution_KeepsExecutionAndRestoresOnDispose()
    {
        var execution = NewExecution();

        using (RuntimeReadScope.BeginExecution(execution))
        {
            Assert.Same(execution, RuntimeReadScope.Current!.Execution);
            Assert.Null(RuntimeReadScope.Current.NodePath);

            using (RuntimeReadScope.BeginNode("GetRtEntitiesByType@1", "PipelineExecution/GetRtEntitiesByType@1"))
            {
                var caller = RuntimeReadScope.Current!;
                Assert.Same(execution, caller.Execution);
                Assert.Equal("GetRtEntitiesByType@1", caller.NodeType);
                Assert.Equal("PipelineExecution/GetRtEntitiesByType@1", caller.NodePath);
                Assert.False(caller.Suppressed);
            }

            Assert.Same(execution, RuntimeReadScope.Current!.Execution);
            Assert.Null(RuntimeReadScope.Current.NodeType);
        }

        Assert.Null(RuntimeReadScope.Current);
    }

    [Fact]
    public void BeginNode_NodeKey_DefaultsToPath_AndKeepsAnExplicitKey()
    {
        using (RuntimeReadScope.BeginNode("Node@1", "PipelineExecution/Node@1"))
        {
            Assert.Equal("PipelineExecution/Node@1", RuntimeReadScope.Current!.NodeKey);
        }

        using (RuntimeReadScope.BeginNode("Node@1", "PipelineExecution/Node@1", "0:PipelineExecution/1:Node@1"))
        {
            Assert.Equal("0:PipelineExecution/1:Node@1", RuntimeReadScope.Current!.NodeKey);
            using (RuntimeReadScope.Suppress())
            {
                Assert.Equal("0:PipelineExecution/1:Node@1", RuntimeReadScope.Current!.NodeKey);
            }
        }
    }

    [Fact]
    public void BeginNode_WithoutExecution_HasNoExecution()
    {
        using (RuntimeReadScope.BeginNode("ForEach@1", "PipelineExecution/ForEach@1"))
        {
            Assert.Null(RuntimeReadScope.Current!.Execution);
            Assert.Equal("ForEach@1", RuntimeReadScope.Current.NodeType);
        }
    }

    [Fact]
    public void Suppress_KeepsCallerAndMarksSuppressed_NestedNodeStaysSuppressed()
    {
        var execution = NewExecution();
        using var executionScope = RuntimeReadScope.BeginExecution(execution);
        using var nodeScope = RuntimeReadScope.BeginNode("ExportDataPointMappings@1", "PipelineExecution/Export");

        using (RuntimeReadScope.Suppress())
        {
            Assert.True(RuntimeReadScope.Current!.Suppressed);
            Assert.Equal("PipelineExecution/Export", RuntimeReadScope.Current.NodePath);
            using (RuntimeReadScope.BeginNode("Inner@1", "PipelineExecution/Export/Inner"))
            {
                Assert.True(RuntimeReadScope.Current!.Suppressed);
                Assert.Same(execution, RuntimeReadScope.Current.Execution);
            }
        }

        Assert.False(RuntimeReadScope.Current!.Suppressed);
    }

    [Fact]
    public async Task Scope_FlowsAcrossAwaitAndIntoStartedTasks_ButNotBackToTheCaller()
    {
        var execution = NewExecution();
        using var executionScope = RuntimeReadScope.BeginExecution(execution);

        await Task.Yield();
        Assert.Same(execution, RuntimeReadScope.Current!.Execution);

        var seenInTask = await Task.Run(() => RuntimeReadScope.Current?.Execution);
        Assert.Same(execution, seenInTask);

        // A scope opened inside an awaited async method does not leak into the caller after it returns.
        await OpenNodeScopeWithoutDisposingAsync();
        Assert.Null(RuntimeReadScope.Current!.NodePath);
    }

    [Fact]
    public void Dispose_Twice_RestoresOnlyOnce()
    {
        var outer = RuntimeReadScope.BeginNode("Outer@1", "Outer");
        var inner = RuntimeReadScope.BeginNode("Inner@1", "Outer/Inner");

        inner.Dispose();
        inner.Dispose();
        Assert.Equal("Outer", RuntimeReadScope.Current!.NodePath);

        outer.Dispose();
        Assert.Null(RuntimeReadScope.Current);
    }

    [Fact]
    public void TryMarkReported_ReturnsTrueOncePerNodePath()
    {
        var execution = NewExecution();

        Assert.True(execution.TryMarkReported("A"));
        Assert.False(execution.TryMarkReported("A"));
        Assert.True(execution.TryMarkReported("B"));
        Assert.True(execution.TryMarkReported(null));
        Assert.False(execution.TryMarkReported(null));
    }

    [Fact]
    public void TryMarkReported_ConcurrentCalls_OnlyOneWins()
    {
        var execution = NewExecution();
        var wins = 0;

        Parallel.For(0, 64, _ =>
        {
            if (execution.TryMarkReported("Loop/Node"))
            {
                Interlocked.Increment(ref wins);
            }
        });

        Assert.Equal(1, wins);
    }

    [Fact]
    public void BeginExecution_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => RuntimeReadScope.BeginExecution(null!));
    }

    private static async Task OpenNodeScopeWithoutDisposingAsync()
    {
        await Task.Yield();
        _ = RuntimeReadScope.BeginNode("Leaky@1", "Leaky");
        Assert.Equal("Leaky", RuntimeReadScope.Current!.NodePath);
    }
}

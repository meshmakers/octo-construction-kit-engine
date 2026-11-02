using System.Collections.Concurrent;
using System.Reflection;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
///     Recognises an exception after which a write may or may not have taken effect: the database did not acknowledge it,
///     but did not refuse it either. The one classification of such writes for every host (AB#8037, rule R3 of the method
///     consistency model): the CK method host of <c>octo-common-services</c> answers <c>METHOD_OUTCOME_UNKNOWN</c> on it,
///     and the pipeline hosts (mesh adapter, SDK adapters) report it as the structured failure kind of the execution, so
///     a pipeline method answers alike.
/// </summary>
/// <remarks>
///     <para>
///         Recognised, anywhere in the chain of inner exceptions (the persistence layer wraps driver exceptions, e.g. into
///         <c>OperationFailedException</c>), and in every inner exception of an <see cref="AggregateException" />:
///     </para>
///     <list type="bullet">
///         <item>
///             a write-concern error of MongoDB: <c>MongoWriteConcernException</c> (commands, transaction commit) - except
///             its subclass <c>MongoDuplicateKeyException</c>, a refused write -, <c>MongoWriteException</c> /
///             <c>MongoBulkWriteException</c> that carry a <c>WriteConcernError</c>, and <c>ClientBulkWriteException</c>
///             with a non-empty <c>WriteConcernErrors</c> list (<c>IMongoClient.BulkWrite</c>). The
///             repositories write with <c>w:majority</c> and a <c>wtimeout</c>; a write-concern error (the timeout, code 64,
///             or any other) does not undo the write on the primary, it is only not confirmed by a majority;
///         </item>
///         <item>
///             a MongoDB exception with the error label <c>UnknownTransactionCommitResult</c> (the driver sets it when a
///             commit may have succeeded on the server: write-concern error, network error or timeout during the commit).
///         </item>
///     </list>
///     <para>
///         Everything else - including a write error without a write-concern error (the write was refused) - is not
///         unknown: the caller reports an ordinary failure.
///     </para>
///     <para>
///         This assembly deliberately does not reference the MongoDB driver (neither do the SDKs and the CK method host
///         that call it), so the driver types are recognised by their full names and the public members
///         <c>WriteConcernError</c> and <c>ErrorLabels</c>. The tests build the real driver exceptions, so a rename in a
///         driver update turns them red.
///     </para>
/// </remarks>
public static class UnacknowledgedWriteClassifier
{
    /// <summary>The driver's label for a commit whose result is unknown.</summary>
    public const string UnknownTransactionCommitResultLabel = "UnknownTransactionCommitResult";

    private const string DriverNamespacePrefix = "MongoDB.Driver";
    private const string WriteConcernExceptionTypeName = "MongoDB.Driver.MongoWriteConcernException";

    // derives from MongoWriteConcernException, but the server refused the write (E11000)
    private const string DuplicateKeyExceptionTypeName = "MongoDB.Driver.MongoDuplicateKeyException";
    private const string WriteConcernErrorProperty = "WriteConcernError";
    private const string WriteConcernErrorsProperty = "WriteConcernErrors";
    private const string ErrorLabelsProperty = "ErrorLabels";

    // a guard against pathological (cyclic or very deep) exception chains
    private const int MaxDepth = 32;

    private static readonly ConcurrentDictionary<Type, DriverMembers> MembersByType = new();

    /// <summary>
    ///     <c>true</c> when <paramref name="exception" /> (or an exception it wraps) says that a write may have taken effect
    ///     without being acknowledged.
    /// </summary>
    /// <param name="exception">The exception to classify; <c>null</c> is never unknown.</param>
    public static bool IsOutcomeUnknown(Exception? exception)
    {
        var pending = new Stack<(Exception Exception, int Depth)>();
        // an instance reachable over several paths (AggregateException(previous, previous)) is classified once
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        if (exception is not null)
        {
            pending.Push((exception, 0));
        }

        while (pending.Count > 0)
        {
            var (current, depth) = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            if (IsUnacknowledgedWrite(current))
            {
                return true;
            }

            if (depth >= MaxDepth)
            {
                continue;
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    pending.Push((inner, depth + 1));
                }
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push((inner, depth + 1));
            }
        }

        return false;
    }

    private static bool IsUnacknowledgedWrite(Exception exception)
    {
        var members = MembersByType.GetOrAdd(exception.GetType(), DriverMembers.Of);
        if (!members.IsDriverException)
        {
            return false;
        }

        if (members.IsWriteConcernException && !members.IsDuplicateKeyException)
        {
            return true;
        }

        try
        {
            if (members.WriteConcernError?.GetValue(exception) is not null)
            {
                return true;
            }

            if (members.WriteConcernErrors?.GetValue(exception) is System.Collections.IEnumerable errors &&
                errors.GetEnumerator().MoveNext())
            {
                return true;
            }

            return members.ErrorLabels?.GetValue(exception) is IEnumerable<string> labels &&
                   labels.Contains(UnknownTransactionCommitResultLabel, StringComparer.Ordinal);
        }
        catch (TargetInvocationException)
        {
            // a getter that throws tells us nothing; the exception stays an ordinary failure
            return false;
        }
    }

    private sealed record DriverMembers(
        bool IsDriverException,
        bool IsWriteConcernException,
        bool IsDuplicateKeyException,
        PropertyInfo? WriteConcernError,
        PropertyInfo? WriteConcernErrors,
        PropertyInfo? ErrorLabels)
    {
        private static readonly DriverMembers None = new(false, false, false, null, null, null);

        public static DriverMembers Of(Type type)
        {
            var isDriver = false;
            var isWriteConcern = false;
            var isDuplicateKey = false;
            for (var t = type; t is not null && t != typeof(Exception); t = t.BaseType)
            {
                if (t.Namespace?.StartsWith(DriverNamespacePrefix, StringComparison.Ordinal) == true)
                {
                    isDriver = true;
                }

                if (string.Equals(t.FullName, WriteConcernExceptionTypeName, StringComparison.Ordinal))
                {
                    isWriteConcern = true;
                }

                if (string.Equals(t.FullName, DuplicateKeyExceptionTypeName, StringComparison.Ordinal))
                {
                    isDuplicateKey = true;
                }
            }

            if (!isDriver)
            {
                return None;
            }

            return new DriverMembers(true, isWriteConcern, isDuplicateKey, Property(type, WriteConcernErrorProperty),
                Property(type, WriteConcernErrorsProperty), Property(type, ErrorLabelsProperty));
        }

        // GetProperty(name) throws on a property a derived type hides with "new"; the first match is the most derived one
        private static PropertyInfo? Property(Type type, string name) =>
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(p => p.Name == name && p.GetIndexParameters().Length == 0);
    }
}

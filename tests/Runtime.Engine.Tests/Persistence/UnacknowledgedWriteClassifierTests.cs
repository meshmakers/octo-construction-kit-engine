using System.Reflection;
using Meshmakers.Octo.Runtime.Contracts;
using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Persistence;

/// <summary>
///     AB#8037 (rule R3): one classification of writes the database did not acknowledge (they may still be on the primary).
///     Every host - the CK method host, the mesh adapter, the SDK adapters - asks this classifier, so a service method and a
///     pipeline method answer alike. The faults are the real MongoDB driver exceptions, so a rename in a driver update turns
///     these tests red.
/// </summary>
public class UnacknowledgedWriteClassifierTests
{
    public static TheoryData<string> UnknownFaults => new()
    {
        nameof(MongoWriteFaults.WriteConcernTimeoutOnCommand),
        nameof(MongoWriteFaults.WriteConcernTimeoutOnSingleWrite),
        nameof(MongoWriteFaults.WriteConcernTimeoutOnBulkWrite),
        nameof(MongoWriteFaults.CommitNetworkErrorWithUnknownResult),
        nameof(MongoWriteFaults.ClientBulkWriteConcernTimeout)
    };

    private static Exception Fault(string name) => name switch
    {
        nameof(MongoWriteFaults.WriteConcernTimeoutOnCommand) => MongoWriteFaults.WriteConcernTimeoutOnCommand(),
        nameof(MongoWriteFaults.WriteConcernTimeoutOnSingleWrite) => MongoWriteFaults.WriteConcernTimeoutOnSingleWrite(),
        nameof(MongoWriteFaults.WriteConcernTimeoutOnBulkWrite) => MongoWriteFaults.WriteConcernTimeoutOnBulkWrite(),
        nameof(MongoWriteFaults.CommitNetworkErrorWithUnknownResult) => MongoWriteFaults.CommitNetworkErrorWithUnknownResult(),
        nameof(MongoWriteFaults.ClientBulkWriteConcernTimeout) => MongoWriteFaults.ClientBulkWriteConcernTimeout(),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [MemberData(nameof(UnknownFaults))]
    public void UnacknowledgedWrite_IsOutcomeUnknown(string fault) =>
        Assert.True(UnacknowledgedWriteClassifier.IsOutcomeUnknown(Fault(fault)));

    [Theory]
    [MemberData(nameof(UnknownFaults))]
    public void UnacknowledgedWrite_WrappedByThePersistenceLayer_OrAPipeline_IsOutcomeUnknown(string fault)
    {
        // the repositories wrap driver exceptions (OperationFailedException keeps the inner one), the pipeline task
        // surfaces them as they are or inside an AggregateException
        var wrapped = new InvalidOperationException("Database operation 'ApplyChangesAsync' failed.",
            new InvalidOperationException("inner wrapper", Fault(fault)));

        Assert.True(UnacknowledgedWriteClassifier.IsOutcomeUnknown(wrapped));
        Assert.True(UnacknowledgedWriteClassifier.IsOutcomeUnknown(new AggregateException(new TimeoutException(), wrapped)));
        Assert.True(UnacknowledgedWriteClassifier.IsOutcomeUnknown(
            new TargetInvocationException(new AggregateException(wrapped))));
    }

    [Fact]
    public void RefusedWrite_OrdinaryException_OrNothing_IsNotOutcomeUnknown()
    {
        Assert.False(UnacknowledgedWriteClassifier.IsOutcomeUnknown(MongoWriteFaults.DuplicateKey()));
        Assert.False(UnacknowledgedWriteClassifier.IsOutcomeUnknown(MongoWriteFaults.BadValue()));
        // a subclass of MongoWriteConcernException, but a refused write (review of PR #333)
        Assert.False(UnacknowledgedWriteClassifier.IsOutcomeUnknown(MongoWriteFaults.DuplicateKeyAsWriteConcernSubclass()));
        Assert.False(UnacknowledgedWriteClassifier.IsOutcomeUnknown(MongoWriteFaults.ClientBulkWriteWithoutWriteConcernError()));
        Assert.False(UnacknowledgedWriteClassifier.IsOutcomeUnknown(new InvalidOperationException("db is down")));
        Assert.False(UnacknowledgedWriteClassifier.IsOutcomeUnknown(new AggregateException()));
        Assert.False(UnacknowledgedWriteClassifier.IsOutcomeUnknown(null));
    }

    [Fact]
    public void ADriverLookalikeOutsideTheDriverNamespace_IsNotOutcomeUnknown()
    {
        // a WriteConcernError property alone is not enough: only types of the MongoDB.Driver namespaces are judged
        Assert.False(UnacknowledgedWriteClassifier.IsOutcomeUnknown(new LookalikeException()));
    }

    [Fact]
    public void AVeryDeepChain_IsWalkedWithinItsLimit_AndDoesNotThrow()
    {
        Exception chain = MongoWriteFaults.WriteConcernTimeoutOnCommand();
        for (var i = 0; i < 100; i++)
        {
            chain = new InvalidOperationException("wrapper " + i, chain);
        }

        // beyond the depth guard the driver exception is not reached; the walk ends without an exception
        Assert.False(UnacknowledgedWriteClassifier.IsOutcomeUnknown(chain));
    }

    [Fact]
    public void ASharedInnerException_IsVisitedOnce_NotOncePerPath()
    {
        // 31 aggregates over the same instance twice: 2^31 paths without a visited set, 32 nodes with one
        Exception shared = new InvalidOperationException("shared, not a write fault");
        for (var i = 0; i < 31; i++)
        {
            shared = new AggregateException(shared, shared);
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var unknown = UnacknowledgedWriteClassifier.IsOutcomeUnknown(shared);

        Assert.False(unknown);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), "the walk is linear in distinct exceptions");
    }

    [Fact]
    public void TheAssembly_DoesNotReferenceTheMongoDbDriver()
    {
        var references = typeof(UnacknowledgedWriteClassifier).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, r => r.Name?.StartsWith("MongoDB", StringComparison.Ordinal) == true);
    }

    private sealed class LookalikeException() : Exception("lookalike")
    {
        // ReSharper disable once UnusedMember.Local - read by reflection in the classifier
        public object WriteConcernError { get; } = new();
    }
}

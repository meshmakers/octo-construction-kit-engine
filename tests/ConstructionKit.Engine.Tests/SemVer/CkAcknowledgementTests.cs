using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using static Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows.RowTestSupport;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;

/// <summary>
///     AB#6295: matching <c>compatibility.acknowledge</c> entries with the classified changes of a release.
/// </summary>
public class CkAcknowledgementTests
{
    private static CkTypeIndexDto UniqueIndex() => new()
    {
        IndexType = IndexTypeDto.Unique, Fields = [new CkIndexFieldsDto { AttributePaths = ["serialNumber"] }]
    };

    private static IReadOnlyList<CkClassifiedModelChange> UniqueIndexOnStableBase()
    {
        var current = Model();
        Machine(current).Indexes!.Add(UniqueIndex());
        return Classify(Model(), current);
    }

    private static CkCompatibilityDto Entries(params (string Change, string Reason)[] entries) => new()
    {
        Acknowledge = entries.Select(e => new CkAcknowledgeDto { Change = e.Change, Reason = e.Reason }).ToList()
    };

    [Fact]
    public void The_key_of_an_added_index_names_the_definition_and_contains_no_version()
    {
        var change = Assert.Single(UniqueIndexOnStableBase());

        var key = CkChangeKey.Of(change.Change);

        Assert.StartsWith("TypeIndex:", key);
        Assert.Contains("#Added:", key);
        Assert.DoesNotContain("1.0.0", key);
        Assert.Contains($"\"{key}\"", CkChangeKey.ExampleEntry(change.Change));
    }

    [Fact]
    public void A_change_that_needs_an_acknowledgement_and_has_none_is_unacknowledged()
    {
        var changes = UniqueIndexOnStableBase();

        var result = CkAcknowledgementResult.Evaluate(changes, null, isEnforced: true);

        var missing = Assert.Single(result.Unacknowledged);
        Assert.Equal(CkChangeKey.Of(changes[0].Change), missing.Key);
        Assert.Empty(result.Acknowledged);
        Assert.Empty(result.Stale);
        Assert.True(result.HasFindings);
    }

    [Fact]
    public void A_matching_entry_acknowledges_exactly_that_change_and_keeps_the_level()
    {
        var changes = UniqueIndexOnStableBase();
        var key = CkChangeKey.Of(changes[0].Change);

        var result = CkAcknowledgementResult.Evaluate(changes, Entries((key, "cleaned up")), isEnforced: true);

        var acknowledged = Assert.Single(result.Acknowledged);
        Assert.Equal("cleaned up", acknowledged.Reason);
        Assert.Equal(CkSemVerLevel.Major, acknowledged.Change.Level);
        Assert.False(result.HasFindings);
    }

    [Fact]
    public void Matching_is_exact_and_case_sensitive()
    {
        var changes = UniqueIndexOnStableBase();
        var key = CkChangeKey.Of(changes[0].Change);

        var result = CkAcknowledgementResult.Evaluate(changes, Entries((key.ToLowerInvariant(), "x")), true);

        Assert.Single(result.Unacknowledged);
        Assert.Single(result.Stale);
    }

    [Fact]
    public void An_entry_for_an_ordinary_change_is_stale_and_names_the_change()
    {
        var current = Model();
        Machine(current).IsFinal = true;
        var changes = Classify(Model(), current);
        var ordinary = Assert.Single(changes);
        Assert.False(ordinary.RequiresAcknowledge);

        var result = CkAcknowledgementResult.Evaluate(changes, Entries((CkChangeKey.Of(ordinary.Change), "x")), true);

        var stale = Assert.Single(result.Stale);
        Assert.Same(ordinary, stale.MatchedChange);
        Assert.Empty(result.Acknowledged);
    }

    [Fact]
    public void A_ckLanguage_1_model_is_not_enforced()
    {
        var result = CkAcknowledgementResult.Evaluate(UniqueIndexOnStableBase(), null, isEnforced: false);

        Assert.False(result.IsEnforced);
        Assert.False(result.HasFindings);
    }

    [Fact]
    public void The_changelog_without_acknowledgements_is_byte_identical_and_with_them_lists_change_level_and_reason()
    {
        var changes = UniqueIndexOnStableBase();
        var key = CkChangeKey.Of(changes[0].Change);
        var acknowledged = CkAcknowledgementResult.Evaluate(changes, Entries((key, "cleaned up")), true).Acknowledged;
        var generator = new CkChangelogGenerator();
        var date = new DateTime(2026, 10, 10);
        var version = new CkVersion(2, 0, 0);

        var plain = generator.Generate(null, version, date, CkSemVerLevel.Major, changes);
        var emptyList = generator.Generate(null, version, date, CkSemVerLevel.Major, changes, null, []);
        var withAck = generator.Generate(null, version, date, CkSemVerLevel.Major, changes, null, acknowledged);

        Assert.Equal(plain, emptyList);
        Assert.DoesNotContain("Acknowledged changes", plain);
        Assert.Contains("### Acknowledged changes", withAck);
        Assert.Contains("_(major — acknowledged: cleaned up)_", withAck);
        // Idempotent for the same version and input
        Assert.Equal(withAck, generator.Generate(withAck, version, date, CkSemVerLevel.Major, changes, null, acknowledged));
    }

    [Fact]
    public void Index_modifications_of_one_type_have_distinct_keys()
    {
        CkModelChange Change(string oldValue, string newValue) => new()
        {
            ChangeKind = CkModelChangeKind.Modified, ElementKind = CkModelElementKind.TypeIndex,
            ElementId = "Machine-1/index", Property = "indexType", OldValue = oldValue, NewValue = newValue
        };

        Assert.NotEqual(CkChangeKey.Of(Change("Ascending on Serial", "Unique on Serial")),
            CkChangeKey.Of(Change("Ascending on Tag", "Unique on Tag")));
    }
}

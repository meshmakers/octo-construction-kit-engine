using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;
using Meshmakers.Octo.Runtime.Contracts.Secrets;
using Meshmakers.Octo.Runtime.Engine.Secrets;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Secrets;

/// <summary>
///     AB#5532: the comparison behind the conditional (compare-and-swap) rewrite of the secret sweep.
/// </summary>
public class StoredAttributeValueComparerTests
{
    private static readonly SecretAttributeProtector Protector = SecretTestModel.CreateProtector();
    private static readonly string EnvelopeA = Protector.Protect("a").Envelope!;
    private static readonly string EnvelopeB = Protector.Protect("a").Envelope!; // same plaintext, new nonce

    private readonly SecretTestModel _model = new();

    [Fact]
    public void ProtectedValues_CompareByEnvelope()
    {
        Assert.True(StoredAttributeValueComparer.AreEqual(RtSecretValue.Protected(EnvelopeA),
            RtSecretValue.Protected(EnvelopeA)));
        Assert.False(StoredAttributeValueComparer.AreEqual(RtSecretValue.Protected(EnvelopeA),
            RtSecretValue.Protected(EnvelopeB)));
    }

    [Fact]
    public void LegacyValues_CompareByText_WhateverTheirClrShape()
    {
        Assert.True(StoredAttributeValueComparer.AreEqual("plain", RtSecretValue.LegacyPlaintext("plain")));
        Assert.True(StoredAttributeValueComparer.AreEqual(RtSecretValue.LegacyPlaintext("plain"), "plain"));
        Assert.False(StoredAttributeValueComparer.AreEqual("plain", RtSecretValue.LegacyPlaintext("other")));
    }

    [Fact]
    public void ProtectedValue_NeverEqualsAString_EvenWithTheSameEnvelope()
    {
        // Sub-document vs. string slot: the sweep moves a string-stored envelope to the protected form.
        Assert.False(StoredAttributeValueComparer.AreEqual(EnvelopeA, RtSecretValue.Protected(EnvelopeA)));
        Assert.False(StoredAttributeValueComparer.AreEqual(RtSecretValue.Protected(EnvelopeA), EnvelopeA));
    }

    [Fact]
    public void Null_OnlyEqualsNull()
    {
        Assert.True(StoredAttributeValueComparer.AreEqual(null, null));
        Assert.False(StoredAttributeValueComparer.AreEqual(null, "x"));
        Assert.False(StoredAttributeValueComparer.AreEqual(RtSecretValue.Protected(EnvelopeA), null));
    }

    [Fact]
    public void RecordArrays_CompareAsAWhole_AcrossCollectionTypes()
    {
        object stored = new List<object>
        {
            _model.CredentialRecord("a", "plain"),
            _model.CredentialRecord("b", RtSecretValue.Protected(EnvelopeA))
        };
        object expected = new List<RtRecord>
        {
            _model.CredentialRecord("a", RtSecretValue.LegacyPlaintext("plain")),
            _model.CredentialRecord("b", RtSecretValue.Protected(EnvelopeA))
        };

        Assert.True(StoredAttributeValueComparer.AreEqual(stored, expected));
    }

    [Fact]
    public void RecordArrays_DifferInANonSecretMember_OrInLength()
    {
        var expected = new List<RtRecord> { _model.CredentialRecord("a", "plain") };

        var renamed = new List<RtRecord> { _model.CredentialRecord("a2", "plain") };
        var longer = new List<RtRecord> { _model.CredentialRecord("a", "plain"), _model.CredentialRecord("b", null) };
        var withNote = new List<RtRecord> { _model.CredentialRecord("a", "plain") };
        withNote[0].SetAttributeRawValue("Note", "added");

        Assert.False(StoredAttributeValueComparer.AreEqual(renamed, expected));
        Assert.False(StoredAttributeValueComparer.AreEqual(longer, expected));
        Assert.False(StoredAttributeValueComparer.AreEqual(withNote, expected));
    }

    [Fact]
    public void RecordMember_MissingEqualsNull()
    {
        var withoutValue = _model.CredentialRecord("a", null, includeValue: false);
        var withNull = _model.CredentialRecord("a", null);

        Assert.True(StoredAttributeValueComparer.AreEqual(withoutValue, withNull));
    }

    [Fact]
    public void Numbers_CompareByValue()
    {
        Assert.True(StoredAttributeValueComparer.AreEqual(5, 5L));
        Assert.True(StoredAttributeValueComparer.AreEqual(2.5d, 2.5f));
        Assert.False(StoredAttributeValueComparer.AreEqual(5, 6L));
    }

    [Fact]
    public void SweepCopy_OfARecordArray_EqualsTheOriginal_AndIsIndependent()
    {
        var original = new List<RtRecord> { _model.CredentialRecord("a", "plain") };

        var copy = SecretMaintenanceService.CopyAttributeValue(original);
        original[0].SetAttributeRawValue("Value", RtSecretValue.Protected(EnvelopeA));

        Assert.False(StoredAttributeValueComparer.AreEqual(original, copy));
        Assert.True(StoredAttributeValueComparer.AreEqual(
            new List<RtRecord> { _model.CredentialRecord("a", "plain") }, copy));
    }
}

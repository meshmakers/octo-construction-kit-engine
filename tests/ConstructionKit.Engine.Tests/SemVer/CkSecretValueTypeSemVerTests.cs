using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;

/// <summary>
///     AB#5531 / AB#5528 decision 2: String -> Secret is Minor, every other value-type change stays
///     Major. The record key is a Minor change.
/// </summary>
public class CkSecretValueTypeSemVerTests
{
    private readonly CkModelDiffService _diffService = new();
    private readonly CkSemVerClassifier _classifier = new();

    private IReadOnlyList<CkClassifiedModelChange> Classify(CkCompiledModelRoot baseline, CkCompiledModelRoot current)
    {
        var changes = _diffService.Diff(baseline, current);
        Assert.NotEmpty(changes);
        return _classifier.Classify(changes, baseline, current);
    }

    [Fact]
    public void StringToSecret_IsMinor_WithReason()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(current, "SerialNumber").ValueType = AttributeValueTypesDto.Secret;

        var classified = Classify(baseline, current);

        var valueTypeChange = Assert.Single(classified, c => c.Change.Property == "valueType");
        Assert.Equal(CkSemVerLevel.Minor, valueTypeChange.Level);
        Assert.Contains("String -> Secret", valueTypeChange.Reason);
        Assert.Equal(CkSemVerLevel.Minor, _classifier.GetRequiredLevel(classified));
    }

    [Fact]
    public void SecretToString_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(baseline, "SerialNumber").ValueType = AttributeValueTypesDto.Secret;
        var current = SemVerTestModels.CreateModel();

        var classified = Classify(baseline, current);

        Assert.Equal(CkSemVerLevel.Major,
            Assert.Single(classified, c => c.Change.Property == "valueType").Level);
    }

    [Fact]
    public void IntToSecret_IsMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(current, "WithDefault").ValueType = AttributeValueTypesDto.Secret;

        var classified = Classify(baseline, current);

        Assert.Equal(CkSemVerLevel.Major,
            Assert.Single(classified, c => c.Change.Property == "valueType").Level);
    }

    [Fact]
    public void StringToInt_StaysMajor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetAttribute(current, "SerialNumber").ValueType = AttributeValueTypesDto.Int;

        Assert.Equal(CkSemVerLevel.Major, _classifier.GetRequiredLevel(Classify(baseline, current)));
    }

    [Fact]
    public void RecordKeyAdded_IsMinor()
    {
        var baseline = SemVerTestModels.CreateModel();
        var current = SemVerTestModels.CreateModel();
        SemVerTestModels.GetRecord(current).RecordKey = "Street";

        var classified = Classify(baseline, current);

        var change = Assert.Single(classified);
        Assert.Equal("recordKey", change.Change.Property);
        Assert.Equal(CkSemVerLevel.Minor, change.Level);
    }
}

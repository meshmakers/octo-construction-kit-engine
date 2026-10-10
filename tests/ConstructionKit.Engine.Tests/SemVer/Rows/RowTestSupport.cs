using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.CkV2;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer.Rows;

/// <summary>
///     Shared helpers of the rule-row tests. Every row test is named after its row id in docs/ck-semver-rules.md
///     (<c>N1_…</c>); the classification guard checks both directions.
/// </summary>
internal static class RowTestSupport
{
    public const string M = SemVerTestModels.ModelName;

    public static IReadOnlyList<CkClassifiedModelChange> Classify(CkCompiledModelRoot baseline, CkCompiledModelRoot current)
    {
        var changes = new CkModelDiffService().Diff(baseline, current);
        return new CkSemVerClassifier().Classify(changes, baseline, current);
    }

    public static CkSemVerLevel Level(CkCompiledModelRoot baseline, CkCompiledModelRoot current) =>
        new CkSemVerClassifier().GetRequiredLevel(Classify(baseline, current));

    /// <summary>
    ///     The CK v2 SemVer test model plus a completed interface: an association member and the full method.
    /// </summary>
    public static CkCompiledModelRoot Model()
    {
        var model = CkV2TestModels.CreateModel();
        var serialized = Serialized(model);
        serialized.Associations =
        [
            new CkInterfaceAssociationDto
            {
                CkRoleId = $"{M}/Parent", TargetCkTypeId = $"{M}/Machine", Multiplicity = MultiplicitiesDto.ZeroOrOne,
                IsOptional = true
            }
        ];
        serialized.Methods = [CkV2TestModels.CreateFullMethod()];
        return model;
    }

    public static CkInterfaceDto Serialized(CkCompiledModelRoot model) =>
        model.Interfaces!.Single(i => i.InterfaceId.FullName == "Serialized-1");

    public static CkCompiledTypeDto Machine(CkCompiledModelRoot model) => SemVerTestModels.GetMachine(model);

    /// <summary>
    ///     The full method ChangePassword-1 of the type ("type") or of the interface ("interface").
    /// </summary>
    public static CkMethodDto Method(CkCompiledModelRoot model, string owner) =>
        (owner == "type" ? Machine(model).Methods! : Serialized(model).Methods!).Single(m => m.MethodId == "ChangePassword-1");

    public static List<CkMethodDto> Methods(CkCompiledModelRoot model, string owner) =>
        owner == "type" ? Machine(model).Methods! : Serialized(model).Methods!;
}

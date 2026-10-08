using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.ModelCatalogs;

/// <summary>
///     CK v2 (F1.1-S6, AB#5909): the catalog directory layout. Classic (<c>ckLanguage</c> 1, exact-pinned) models
///     live under <c>ck-models/v2/</c>, which is all an engine before CK v2 reads. <c>ckLanguage: 2</c> and
///     range-retaining models are published under <c>ck-models/v3/</c>, so an older engine never loads a model it
///     would misread (it tolerates unknown properties). Engines from CK v2 on read both roots, the v3 root first.
///     Each root carries its own <c>catalog.json</c> index tree.
/// </summary>
public static class CkCatalogLayout
{
    /// <summary>The root of classic models.</summary>
    public const string V2Root = "ck-models/v2/";

    /// <summary>The root of <c>ckLanguage: 2</c> and range-retaining models.</summary>
    public const string V3Root = "ck-models/v3/";

    /// <summary>The roots a CK v2 engine reads, in lookup order.</summary>
    public static IReadOnlyList<string> ReadRoots { get; } = [V3Root, V2Root];

    /// <summary>True when the model must be published under <see cref="V3Root" />.</summary>
    public static bool RequiresV3(CkCompiledModelRoot model) =>
        model.EffectiveCkLanguage >= 2 || model.IsRangeRetaining || model.MinEngineVersion != null;

    /// <summary>The root a model is published under.</summary>
    public static string GetPublishRoot(CkCompiledModelRoot model) => RequiresV3(model) ? V3Root : V2Root;

    /// <summary>The model directory below a root: <c>&lt;root&gt;&lt;letter&gt;/&lt;Name&gt;/</c>.</summary>
    public static string ModelDirectory(string root, string modelName) =>
        $"{root}{modelName[0].ToString().ToLower()}/{modelName}/";

    /// <summary>The relative path of a compiled model file below a root.</summary>
    public static string ModelFilePath(string root, CkModelId modelId) =>
        $"{ModelDirectory(root, modelId.Name)}{modelId.Version.Major}/{ModelFileName(modelId)}";

    /// <summary>The file name of a compiled model: <c>ck-&lt;name&gt;-&lt;version&gt;.json</c>.</summary>
    public static string ModelFileName(CkModelId modelId) => $"ck-{modelId.Name.ToLower()}-{modelId.Version}.json";
}

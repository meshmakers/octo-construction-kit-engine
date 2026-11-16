using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

/// <summary>
///     AB#9096: finds the generated C# interface (<c>IRt&lt;Name&gt;</c>) of a CK interface that another CK model
///     declares, in the assemblies the compilation references. Every model generates into
///     <c>&lt;RootNamespace&gt;.Generated.&lt;ModelName&gt;.v&lt;Major&gt;</c>; the root namespace of the foreign model's
///     project is not known from the CK cache, so the lookup reads it from the referenced assemblies and the generators
///     emit the fully qualified name (<c>global::…</c>). A foreign interface that no referenced assembly declares (the
///     C# package of that model is not referenced) is collected in <see cref="Unresolved" /> and reported as OM1006.
/// </summary>
public sealed class CkGeneratedInterfaceLookup
{
    internal const string GeneratedSegment = ".Generated.";

    private readonly IReadOnlyList<string> _fullNames;
    private readonly Dictionary<CkId<CkInterfaceId>, string> _unresolved = new();

    /// <summary>Creates a lookup over the metadata names of generated interfaces (<c>A.B.Generated.System.v2.IRtNamed</c>).</summary>
    public CkGeneratedInterfaceLookup(IEnumerable<string> fullNames)
    {
        _fullNames = fullNames.ToList();
    }

    /// <summary>The foreign interfaces that could not be resolved, with the reason.</summary>
    public IReadOnlyDictionary<CkId<CkInterfaceId>, string> Unresolved => _unresolved;

    /// <summary>
    ///     The C# reference of a CK interface used in code generated for <paramref name="ownModel" />: the short name
    ///     (<c>IRtNamed</c>) for an interface of the own model — unchanged output — and the fully qualified name
    ///     (<c>global::Ns.Generated.System.v2.IRtNamed</c>) for an interface of another model. Without a lookup, or when
    ///     the foreign interface is not found, the short name is returned (and the miss is recorded).
    /// </summary>
    public static string Reference(CkId<CkInterfaceId> ckInterfaceId, CkModelId ownModel,
        CkGeneratedInterfaceLookup? lookup)
    {
        var shortName = CkInterfaceCodeGenerator.InterfaceName(ckInterfaceId.ElementId);
        if (lookup == null || string.Equals(ckInterfaceId.ModelId.Name, ownModel.Name, StringComparison.Ordinal))
        {
            return shortName;
        }

        return lookup.Find(ckInterfaceId) ?? shortName;
    }

    /// <summary>
    ///     The fully qualified C# name (<c>global::…</c>) of a foreign CK interface, or <c>null</c> when no referenced
    ///     assembly, or more than one, declares it for the model's major version.
    /// </summary>
    public string? Find(CkId<CkInterfaceId> ckInterfaceId)
    {
        var suffix =
            $"{GeneratedSegment}{ckInterfaceId.ModelId.Name}.v{ckInterfaceId.ModelId.Version.Major}.{CkInterfaceCodeGenerator.InterfaceName(ckInterfaceId.ElementId)}";
        // A leading dot, so a project whose root namespace is empty ("Generated.System.v2…") matches too.
        var matches = _fullNames.Where(n => ("." + n).EndsWith(suffix, StringComparison.Ordinal)).ToList();
        if (matches.Count == 1)
        {
            return "global::" + matches[0];
        }

        var modelName = ckInterfaceId.ModelId.Name;
        _unresolved[ckInterfaceId] = matches.Count == 0
            ? $"no referenced assembly declares the generated interface '*{suffix}'; add a reference to the C# package of model '{modelName}'" +
              (modelName == "System" ? " (Meshmakers.Octo.ConstructionKit.Models.System)" : "")
            : $"more than one referenced assembly declares it ({string.Join(", ", matches)}); keep only one reference to the C# package of model '{modelName}'";
        return null;
    }
}

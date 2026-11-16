using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

/// <summary>
///     AB#9096: reads the generated CK interfaces from the assemblies a compilation references, as input of
///     <see cref="CkGeneratedInterfaceLookup" />.
/// </summary>
public static class CkGeneratedInterfaceCollector
{
    private const string GeneratedSegment = CkGeneratedInterfaceLookup.GeneratedSegment;

    /// <summary>
    ///     Collects the metadata names of all public generated CK interfaces (<c>IRt*</c> in a namespace
    ///     <c>*.Generated.*.v&lt;n&gt;</c>) of the assemblies <paramref name="compilation" /> references, sorted, so the
    ///     result can serve as an equatable incremental-generator input.
    /// </summary>
    public static ImmutableArray<string> Collect(Compilation compilation, CancellationToken cancellationToken = default)
    {
        var result = new List<string>();
        foreach (var reference in compilation.References)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly ||
                !assembly.NamespaceNames.Contains("Generated"))
            {
                continue;
            }

            Walk(assembly.GlobalNamespace, result);
        }

        result.Sort(StringComparer.Ordinal);
        return result.Distinct(StringComparer.Ordinal).ToImmutableArray();
    }

    private static void Walk(INamespaceSymbol ns, List<string> result)
    {
        foreach (var child in ns.GetNamespaceMembers())
        {
            Walk(child, result);
        }

        if (ns.IsGlobalNamespace)
        {
            return;
        }

        var nsName = ns.ToDisplayString();
        if (nsName.IndexOf(GeneratedSegment, StringComparison.Ordinal) < 0 || !IsMajorSegment(ns.Name))
        {
            return;
        }

        foreach (var type in ns.GetTypeMembers())
        {
            if (type.TypeKind == TypeKind.Interface && type.DeclaredAccessibility == Accessibility.Public &&
                type.Name.StartsWith("IRt", StringComparison.Ordinal))
            {
                result.Add($"{nsName}.{type.Name}");
            }
        }
    }

    private static bool IsMajorSegment(string name) =>
        name.Length > 1 && name[0] == 'v' && name.Skip(1).All(char.IsDigit);
}

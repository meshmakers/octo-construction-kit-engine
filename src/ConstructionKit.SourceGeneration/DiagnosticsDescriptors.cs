using Microsoft.CodeAnalysis;

namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

internal static class DiagnosticsDescriptors
{
    public static readonly DiagnosticDescriptor EmptyFile
        = new("OM1000", // id
            "Empty file", // title
            "File '{0}' is empty", // message
            "Construction Kit", // category
            DiagnosticSeverity.Error,
            true);

    public static readonly DiagnosticDescriptor GeneratorInfo
        = new("OM1001", // id
            "Source Generator Information", // title
            "{0}", // message
            "Construction Kit", // category
            DiagnosticSeverity.Info,
            true);

    public static readonly DiagnosticDescriptor GeneratorWarning
        = new("OM1002", // id
            "Source Generator Warning", // title
            "{0}", // message
            "Construction Kit", // category
            DiagnosticSeverity.Warning,
            true);

    public static readonly DiagnosticDescriptor GeneratorError
        = new("OM1003", // id
            "Source Generator Error", // title
            "{0}", // message
            "Construction Kit", // category
            DiagnosticSeverity.Error,
            true);

    /// <summary>
    ///     F1.1-S2 (D1 diagnostic): a major-qualified reference (<c>System@2/Enabled-1</c>) of a range-retaining
    ///     model has no matching version in the compile cache, so code generation cannot resolve it.
    /// </summary>
    public static readonly DiagnosticDescriptor UnboundMajorQualifiedReference
        = new("OM1004", // id
            "Unbound major-qualified CK reference", // title
            "{0}", // message
            "Construction Kit", // category
            DiagnosticSeverity.Error,
            true);

    /// <summary>
    ///     AB#9096: a type implements, or an interface extends, a CK interface of another model, and no referenced
    ///     assembly declares its generated C# interface — the C# package of that model is missing.
    /// </summary>
    public static readonly DiagnosticDescriptor UnresolvedForeignInterface
        = new("OM1006", // id
            "Interface of another CK model not found", // title
            "Interface '{0}' used by model '{1}' cannot be referenced in the generated C#: {2}. Add a reference to the C# package of model '{3}' (for System: Meshmakers.Octo.ConstructionKit.Models.System).", // message
            "Construction Kit", // category
            DiagnosticSeverity.Error,
            true);
}

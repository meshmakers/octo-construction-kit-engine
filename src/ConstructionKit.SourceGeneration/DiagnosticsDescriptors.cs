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
}
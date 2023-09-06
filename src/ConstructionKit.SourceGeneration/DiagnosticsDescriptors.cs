using Microsoft.CodeAnalysis;

namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

internal static class DiagnosticsDescriptors
{
    public static readonly DiagnosticDescriptor EmptyFile
        = new("OM1000",                               // id
            "Empty file",           // title
            "File '{0}' is empty", // message
            "Construction Kit",                          // category
            DiagnosticSeverity.Error,
            true);
}
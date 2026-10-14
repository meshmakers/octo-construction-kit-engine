using Microsoft.CodeAnalysis;

namespace Meshmakers.Octo.ConstructionKit.Analyzers;

internal static class DiagnosticsDescriptors
{
    /// <summary>
    ///     AB#6673: run-time code uses a generated <b>versioned</b> CK element id (<c>SystemCkIds.CkEntityTypeId</c>,
    ///     a <c>CkId&lt;T&gt;</c> built from the model version the service was compiled against). A tenant may hold a newer
    ///     System than the service embeds, so the versioned id is not in the tenant's CK cache. Use the version-less
    ///     <c>RtCk…Id</c> constant (or call <c>.ToRtCkId()</c> immediately).
    /// </summary>
    public static readonly DiagnosticDescriptor VersionedGeneratedCkId
        = new("OM1005", // id
            "Generated versioned CK id used at run time", // title
            "'{0}.{1}' is a generated versioned CK id: the tenant may hold a newer model version than this service " +
            "embeds. Use '{0}.{2}' (version-less RtCkId), or convert with .ToRtCkId() right away.", // message
            "Construction Kit", // category
            DiagnosticSeverity.Error,
            true,
            "Generated 'CkIds.Ck<Element>Id' fields carry the model version the service was compiled against. " +
            "Run-time lookups must use the version-less 'RtCk<Element>Id'. Opt out for one project with " +
            "dotnet_diagnostic.OM1005.severity = none.");
}

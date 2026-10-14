using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Meshmakers.Octo.ConstructionKit.Analyzers;

/// <summary>
///     AB#6673 guard: a reference to a generated <b>versioned</b> CK element id (<c>…CkIds.Ck&lt;Element&gt;Id</c>, a
///     <c>CkId&lt;T&gt;</c> bound to the model version the service was compiled against) is reported as <c>OM1005</c> (Info by default, raise to error per project),
///     because run-time lookups against a tenant that holds a newer model version would not find it in the CK cache.
///     <list type="bullet">
///         <item>Allowed: the version-less <c>RtCk…Id</c> constants, the model id <c>CkModelId</c> (import / satisfied-by-name),
///             and a versioned id that is converted at once with <c>.ToRtCkId()</c>.</item>
///         <item>Not analysed: generated code, and projects that are tests (assembly name ends with Tests) - they may build
///             versioned ids on purpose. Anything else opts out with <c>dotnet_diagnostic.OM1005.severity = none</c>.</item>
///         <item>Severity: Info (suggestion) by default - never breaks a build, even with warnings as errors. A repo that has
///             audited itself raises it with <c>dotnet_diagnostic.OM1005.severity = error</c> in <c>.editorconfig</c> or
///             <c>&lt;WarningsAsErrors&gt;OM1005&lt;/WarningsAsErrors&gt;</c> in <c>Directory.Build.props</c>.</item>
///     </list>
///     Ships with the <c>Meshmakers.Octo.ConstructionKit.SourceGeneration</c> package, so every service that generates its
///     CK model code has it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CkVersionedIdAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticsDescriptors.VersionedGeneratedCkId);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var assemblyName = start.Compilation.AssemblyName ?? string.Empty;
            if (assemblyName.EndsWith("Tests", System.StringComparison.Ordinal))
            {
                return;
            }

            start.RegisterOperationAction(Analyze, OperationKind.FieldReference);
        });
    }

    private static void Analyze(OperationAnalysisContext context)
    {
        var reference = (IFieldReferenceOperation)context.Operation;
        var field = reference.Field;
        if (!field.ContainingType.Name.EndsWith("CkIds", System.StringComparison.Ordinal) ||
            !field.Name.StartsWith("Ck", System.StringComparison.Ordinal) ||
            field.Name == "CkModelId" ||
            field.Type is not INamedTypeSymbol { Name: "CkId", IsGenericType: true })
        {
            return;
        }

        // `SystemCkIds.CkAutoIncrementTypeId.ToRtCkId()`: converted to the version-less form at once.
        if (reference.Parent is IInvocationOperation { TargetMethod.Name: "ToRtCkId" } ||
            reference.Parent is IArgumentOperation { Parent: IInvocationOperation { TargetMethod.Name: "ToRtCkId" } })
        {
            return;
        }

        var suggested = "Rt" + field.Name;
        context.ReportDiagnostic(Diagnostic.Create(DiagnosticsDescriptors.VersionedGeneratedCkId,
            reference.Syntax.GetLocation(), field.ContainingType.Name, field.Name, suggested));
    }
}

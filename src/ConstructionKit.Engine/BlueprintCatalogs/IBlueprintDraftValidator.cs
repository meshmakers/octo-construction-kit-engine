using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
///     The one validation core of a blueprint draft (AB#9371): manifest, seed data, Secret seed lint and CK model
///     dependencies incl. their version ranges against the CK model catalogs. Used by octo-bpm (validate, pack, publish),
///     the asset repository (<c>POST {tenantId}/v1/blueprints/validate</c>, install path) and, through that endpoint,
///     the MCP tool <c>validate_blueprint</c>. Writes nothing.
/// </summary>
public interface IBlueprintDraftValidator
{
    /// <summary>
    ///     Validates the blueprint draft in <paramref name="source" />.
    /// </summary>
    /// <param name="source">The files of the draft</param>
    /// <param name="options">Options; <c>null</c> = defaults (no CK catalog check)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The findings; never throws for a broken draft</returns>
    Task<BlueprintDraftValidationResult> ValidateAsync(IBlueprintFileSource source,
        BlueprintValidationOptions? options = null, CancellationToken cancellationToken = default);
}

/// <summary>
///     Options of <see cref="IBlueprintDraftValidator.ValidateAsync" />.
/// </summary>
public sealed class BlueprintValidationOptions
{
    /// <summary>
    ///     The CK model catalogs every <c>ckModelDependencies</c> range must resolve in. <c>null</c> skips the check
    ///     (one info finding, code 165); an empty list checks against all catalogs of the catalog manager.
    /// </summary>
    public IReadOnlyList<string>? CkModelCatalogs { get; set; }

    /// <summary>
    ///     CK models the range check leaves out (service-managed models such as <c>System.StreamData</c> on a tenant
    ///     without stream data). Their other checks still run.
    /// </summary>
    public IReadOnlyCollection<string> IgnoredCkModels { get; set; } = [];

    /// <summary>Runs the Secret seed lint when a resolver is available (default true).</summary>
    public bool LintSecretSeedValues { get; set; } = true;
}

/// <summary>
///     Result of a blueprint draft validation (AB#9371).
/// </summary>
public sealed class BlueprintDraftValidationResult
{
    /// <summary>Creates a result.</summary>
    public BlueprintDraftValidationResult(BlueprintMetaRootDto? blueprintMeta, IReadOnlyList<OperationMessage> findings)
    {
        BlueprintMeta = blueprintMeta;
        Findings = findings;
    }

    /// <summary>The parsed manifest; <c>null</c> when it is missing or cannot be read.</summary>
    public BlueprintMetaRootDto? BlueprintMeta { get; }

    /// <summary>The findings in the order they were found. <see cref="OperationMessage.MessageNumber" /> is the stable code.</summary>
    public IReadOnlyList<OperationMessage> Findings { get; }

    /// <summary>True when no finding is an error.</summary>
    public bool IsValid => Findings.All(f => f.MessageLevel is not (MessageLevel.Error or MessageLevel.FatalError));

    /// <summary>Copies the findings into <paramref name="operationResult" />.</summary>
    public void CopyTo(OperationResult operationResult)
    {
        foreach (var finding in Findings)
        {
            operationResult.AddMessage(finding);
        }
    }
}

using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;

namespace Meshmakers.Octo.Runtime.Contracts.Exchange;

/// <summary>
///     Interface for importing a runtime model from a file.
/// </summary>
public interface IImportRtModelCommand
{
    /// <summary>
    ///     Imports as text
    /// </summary>
    /// <param name="runtimeRepository">The runtime repository</param>
    /// <param name="jsonText">Model as JSON text</param>
    /// <param name="importStrategy">Defines the import strategy</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <param name="blankingPolicy">
    ///     What an Upsert does when a seed value would blank a non-empty value of an attribute the
    ///     blueprint does not own (AB#6313). Default <see cref="RtImportBlankingPolicy.Keep" />.
    /// </param>
    /// <param name="confirmedBlankings">
    ///     Entity/attribute pairs whose blanking is confirmed individually (AB#6315) although
    ///     <paramref name="blankingPolicy" /> is <see cref="RtImportBlankingPolicy.Keep" />.
    /// </param>
    /// <returns></returns>
    Task ImportTextAsync(IRuntimeRepository runtimeRepository, string jsonText, ImportStrategy importStrategy,
        CancellationToken? cancellationToken = null,
        RtImportBlankingPolicy blankingPolicy = RtImportBlankingPolicy.Keep,
        IReadOnlyCollection<RtImportBlankingConfirmation>? confirmedBlankings = null);

    /// <summary>
    ///     Imports a model root
    /// </summary>
    /// <param name="runtimeRepository">The runtime repository</param>
    /// <param name="rtModelRootTc">The model root</param>
    /// <param name="importStrategy">Defines the import strategy</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <param name="blankingPolicy">
    ///     What an Upsert does when a seed value would blank a non-empty value of an attribute the
    ///     blueprint does not own (AB#6313). Default <see cref="RtImportBlankingPolicy.Keep" />.
    /// </param>
    /// <param name="confirmedBlankings">
    ///     Entity/attribute pairs whose blanking is confirmed individually (AB#6315) although
    ///     <paramref name="blankingPolicy" /> is <see cref="RtImportBlankingPolicy.Keep" />.
    /// </param>
    /// <returns></returns>
    Task ImportModelAsync(IRuntimeRepository runtimeRepository, RtModelRootTcDto rtModelRootTc,
        ImportStrategy importStrategy, CancellationToken? cancellationToken = null,
        RtImportBlankingPolicy blankingPolicy = RtImportBlankingPolicy.Keep,
        IReadOnlyCollection<RtImportBlankingConfirmation>? confirmedBlankings = null);

    /// <summary>
    ///     Imports from a file
    /// </summary>
    /// <param name="runtimeRepository">The runtime repository</param>
    /// <param name="filePath">A file path as ZIP (containing YAML or JSON), JSON or YAML files</param>
    /// <param name="contentType">The content type of the file</param>
    /// <param name="importStrategy">Defines the import strategy</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <param name="blankingPolicy">
    ///     What an Upsert does when a seed value would blank a non-empty value of an attribute the
    ///     blueprint does not own (AB#6313). Default <see cref="RtImportBlankingPolicy.Keep" />.
    /// </param>
    /// <param name="confirmedBlankings">
    ///     Entity/attribute pairs whose blanking is confirmed individually (AB#6315) although
    ///     <paramref name="blankingPolicy" /> is <see cref="RtImportBlankingPolicy.Keep" />.
    /// </param>
    /// <returns></returns>
    Task ImportAsync(IRuntimeRepository runtimeRepository, string filePath, string contentType,
        ImportStrategy importStrategy, CancellationToken? cancellationToken = null,
        RtImportBlankingPolicy blankingPolicy = RtImportBlankingPolicy.Keep,
        IReadOnlyCollection<RtImportBlankingConfirmation>? confirmedBlankings = null);

    /// <summary>
    ///     Imports from a file on behalf of a user or client that initiated the import through an API route
    ///     (AB#6392). The session stays the system session, so replace semantics, creator stamping and the
    ///     preserve pass behave exactly as in <see cref="ImportAsync" />; in addition the blueprint-lock protection
    ///     of AB#6384 applies to CK types whose data policy opted in (<c>ProtectBlueprintLocked</c>): a preflight
    ///     over the whole file fails the import atomically, before any write, when an entity that is locked by a
    ///     blueprint would be overwritten (AuditOnly policies write and publish an audit event instead), and the
    ///     blueprint bookkeeping attributes of the file are stripped. Tenants without an opted-in policy pay
    ///     nothing. See <see cref="BlueprintLockSummary" /> for the result.
    /// </summary>
    /// <param name="runtimeRepository">The runtime repository</param>
    /// <param name="filePath">A file path as JSON or YAML file</param>
    /// <param name="contentType">The content type of the file</param>
    /// <param name="importStrategy">Defines the import strategy</param>
    /// <param name="caller">The initiating caller; <see cref="RtImportCallerContext.EnforceBlueprintLock" /> decides</param>
    /// <param name="cancellationToken">An optional cancellation token</param>
    /// <param name="blankingPolicy">See <see cref="ImportAsync" /></param>
    /// <param name="confirmedBlankings">See <see cref="ImportAsync" /></param>
    /// <returns></returns>
    Task ImportAsCallerAsync(IRuntimeRepository runtimeRepository, string filePath, string contentType,
        ImportStrategy importStrategy, RtImportCallerContext caller, CancellationToken? cancellationToken = null,
        RtImportBlankingPolicy blankingPolicy = RtImportBlankingPolicy.Keep,
        IReadOnlyCollection<RtImportBlankingConfirmation>? confirmedBlankings = null);

    /// <summary>
    ///     What the blueprint-lock protection stripped or audited during <see cref="ImportAsCallerAsync" /> (AB#6392).
    ///     <see cref="RtImportBlueprintLockSummary.Empty" /> when nothing happened.
    /// </summary>
    RtImportBlueprintLockSummary BlueprintLockSummary { get; }

    /// <summary>
    ///     The attributes found blanked by seed values during this command's imports (AB#6313), kept
    ///     or applied according to the policy. The command is transient, so this is the report of the
    ///     import(s) run on this instance.
    /// </summary>
    IReadOnlyList<RtImportGuardEntry> GuardEntries { get; }
}

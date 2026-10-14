using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.RangeRetention;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

/// <summary>
///     AB#6683 / AB#6675: audits the <c>ckModel.yaml</c> files below the given paths for dependency ranges that the
///     one-major-per-range rule of range retention rejects (<see cref="CkDependencyRangeRule" />, the rule the compiler
///     enforces). Read-only and standalone: no catalog, no compile. A non-zero exit code means at least one rejected range.
/// </summary>
internal class AuditRangesCommand : CkcCommand
{
    private readonly ICkSerializer _ckSerializer;
    private readonly IArgument _pathArg;

    public AuditRangesCommand(ILogger<AuditRangesCommand> logger, IOptions<OctoToolOptions> options,
        ICkSerializer ckSerializer)
        : base(logger, "AuditRanges",
            "Lists the dependency ranges of ckModel.yaml files that the one-major-per-range rule of range retention rejects (read-only, standalone)",
            options)
    {
        _ckSerializer = ckSerializer;
        _pathArg = CommandArgumentValue.AddArgument("p", "path",
            ["Directory (searched recursively for ckModel.yaml) or ckModel.yaml file; repeat for several."], true, 1, true);
    }

    public override async Task Execute()
    {
        await base.Execute();

        var audit = new CkModelRangeAudit(_ckSerializer);
        var unreadable = new List<string>();
        var entries = new List<CkRangeAuditEntry>();
        foreach (var path in CommandArgumentValue.GetArgumentValue(_pathArg).Values)
        {
            entries.AddRange(await audit.AuditAsync(path, unreadable));
        }

        foreach (var entry in entries)
        {
            Logger.LogInformation("{Verdict,-8} {Model} -> {Range}  ({File})", entry.IsWithinOneMajor ? "OK" : "REJECTED",
                entry.ModelId, entry.Range.FullName, entry.File);
        }

        foreach (var problem in unreadable)
        {
            Logger.LogWarning("Could not read {Problem}", problem);
        }

        var rejected = entries.Where(e => !e.IsWithinOneMajor).ToList();
        Logger.LogInformation("{Files} ckModel.yaml, {Declarations} dependency declaration(s), {Rejected} rejected by the one-major-per-range rule",
            entries.Select(e => e.File).Distinct().Count(), entries.Count, rejected.Count);
        if (rejected.Count > 0)
        {
            throw new ModelValidationException(
                $"{rejected.Count} dependency range(s) admit more than one major version and are rejected by a range-retaining compile: " +
                string.Join("; ", rejected.Select(r => $"{r.ModelId} -> {r.Range.FullName}")) +
                ". Close each range below the next major, e.g. 'System-[2.5,3.0)'.");
        }
    }
}

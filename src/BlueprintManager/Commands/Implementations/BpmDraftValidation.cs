using Meshmakers.Common.CommandLineParser;
using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.BlueprintManager.Commands.Implementations;

/// <summary>
///     The shared blueprint draft validation of <c>validate</c>, <c>pack</c> and <c>publish</c> (AB#9371): runs
///     <see cref="IBlueprintDraftValidator" /> over the blueprint directory and logs every finding with its stable code.
///     Offline by default; <c>--ck-catalogs</c> adds the CK model dependency-range check.
/// </summary>
internal static class BpmDraftValidation
{
    /// <summary>Long name of the option.</summary>
    public const string CkCatalogsOption = "ck-catalogs";

    /// <summary>Registers the optional <c>--ck-catalogs</c> argument on a command.</summary>
    public static IArgument AddCkCatalogsArgument(ICommandArgumentValue commandArgumentValue)
    {
        return commandArgumentValue.AddArgument("k", CkCatalogsOption,
        [
            "Check the CK model dependency ranges against CK model catalogs: 'all' or a comma-separated list of "
            + "catalog names. Default: not checked (offline)."
        ], false, 1);
    }

    /// <summary>Reads <c>--ck-catalogs</c>: <c>null</c> = not used, empty = all catalogs.</summary>
    public static IReadOnlyList<string>? ReadCkCatalogs(ICommandArgumentValue commandArgumentValue, IArgument argument)
    {
        if (!commandArgumentValue.IsArgumentUsed(argument))
        {
            return null;
        }

        var value = commandArgumentValue.GetArgumentScalarValueOrDefault<string>(argument);
        return ParseCkCatalogs(value);
    }

    /// <summary>Parses the value of <c>--ck-catalogs</c>.</summary>
    public static IReadOnlyList<string> ParseCkCatalogs(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return value.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
    }

    /// <summary>
    ///     Validates the blueprint directory and logs the findings. Throws <see cref="BlueprintCatalogException" /> when
    ///     the draft has errors.
    /// </summary>
    public static async Task<BlueprintDraftValidationResult> RunAsync(IBlueprintDraftValidator validator, string path,
        IReadOnlyList<string>? ckCatalogs, ILogger logger)
    {
        var result = await validator.ValidateAsync(new DirectoryBlueprintFileSource(path),
            new BlueprintValidationOptions { CkModelCatalogs = ckCatalogs });

        foreach (var finding in result.Findings)
        {
            var line = FormatFinding(finding);
            switch (finding.MessageLevel)
            {
                case MessageLevel.Error:
                case MessageLevel.FatalError:
                    logger.LogError("{Finding}", line);
                    break;
                case MessageLevel.Warning:
                    logger.LogWarning("{Finding}", line);
                    break;
                default:
                    logger.LogInformation("{Finding}", line);
                    break;
            }
        }

        if (!result.IsValid)
        {
            var errors = result.Findings.Count(f => f.MessageLevel is MessageLevel.Error or MessageLevel.FatalError);
            throw new BlueprintCatalogException($"Blueprint validation failed with {errors} error(s)");
        }

        return result;
    }

    /// <summary>One finding as a log line: <c>[code] location: text</c>.</summary>
    public static string FormatFinding(OperationMessage finding)
    {
        return string.IsNullOrEmpty(finding.Location)
            ? $"[{finding.MessageNumber}] {finding.MessageText}"
            : $"[{finding.MessageNumber}] {finding.Location}: {finding.MessageText}";
    }
}

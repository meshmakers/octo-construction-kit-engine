using Meshmakers.Common.Shared.Services;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;

namespace Meshmakers.Octo.ConstructionKit.Compiler;

/// <summary>
///     AB#6675: <c>-rr</c> / <c>--rangeRetention &lt;value&gt;</c> are gone (range retention is the compiler default), but builds
///     that still pass them must not break: the option and its value are removed from the command line before parsing, and a
///     deprecation warning (<see cref="CkCompilerOptions.RangeRetentionObsoleteCode" />) goes to stderr (stdout carries the
///     compiled file path). The option is no longer defined, so it does not show up in <c>--help</c>.
/// </summary>
internal sealed class ObsoleteOptionFilteringEnvironmentService(IEnvironmentService inner) : IEnvironmentService
{
    public string[] GetCommandLineArgs()
    {
        var args = inner.GetCommandLineArgs();
        var filtered = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            if (IsObsoleteOption(args[i]))
            {
                Console.Error.WriteLine($"warning {CkCompilerOptions.RangeRetentionObsoleteCode}: {CkCompilerOptions.RangeRetentionObsoleteText}");
                // The option takes one value (-rr true / --rangeRetention false).
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                {
                    i++;
                }

                continue;
            }

            filtered.Add(args[i]);
        }

        return filtered.ToArray();
    }

    internal static bool IsObsoleteOption(string argument) =>
        argument.Equals("-rr", StringComparison.OrdinalIgnoreCase) ||
        argument.Equals("--rangeRetention", StringComparison.OrdinalIgnoreCase) ||
        argument.Equals("/rr", StringComparison.OrdinalIgnoreCase);
}

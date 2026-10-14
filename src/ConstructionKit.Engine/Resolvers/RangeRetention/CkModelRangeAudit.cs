using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers.RangeRetention;

/// <summary>One dependency declaration of a <c>ckModel.yaml</c> and its verdict.</summary>
/// <param name="File">Path of the <c>ckModel.yaml</c>.</param>
/// <param name="ModelId">The model that declares the dependency (<c>modelId</c> of the file).</param>
/// <param name="Range">The declared dependency range as written (<c>Basic-[2.4,)</c>).</param>
/// <param name="IsWithinOneMajor"><see cref="CkDependencyRangeRule.IsWithinOneMajor(CkModelIdVersionRange)" /> of the range.</param>
public sealed record CkRangeAuditEntry(string File, string ModelId, CkModelIdVersionRange Range, bool IsWithinOneMajor);

/// <summary>
///     AB#6683 / AB#6675: the audit of producer <c>ckModel.yaml</c> files for dependency ranges that the one-major-per-range
///     rule of range retention rejects (<see cref="CkDependencyRangeRule" />, the rule the compiler enforces, so the audit
///     cannot disagree with it). Read-only; behind <c>octo-ckc -c AuditRanges</c>.
/// </summary>
public sealed class CkModelRangeAudit(ICkSerializer serializer)
{
    private static readonly HashSet<string> SkippedDirectories =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "node_modules", ".git", "TestResults" };

    /// <summary>
    ///     Reads every <c>ckModel.yaml</c> below <paramref name="root" /> (a directory, or the file itself) and returns one
    ///     entry per declared dependency, in file order. A file that cannot be read is reported through
    ///     <paramref name="unreadable" /> with its error.
    /// </summary>
    public async Task<IReadOnlyList<CkRangeAuditEntry>> AuditAsync(string root, ICollection<string>? unreadable = null)
    {
        var entries = new List<CkRangeAuditEntry>();
        foreach (var file in FindModelFiles(root))
        {
            try
            {
                using var stream = File.OpenRead(file);
                var result = new OperationResult();
                var meta = await serializer.DeserializeMetaAsync(stream, file, result).ConfigureAwait(false);
                if (result.HasErrors || result.HasFatalErrors)
                {
                    unreadable?.Add($"{file}: {string.Join("; ", result.Messages.Select(m => m.MessageText))}");
                    continue;
                }

                var modelId = meta.ModelId.FullName;
                foreach (var range in meta.Dependencies ?? [])
                {
                    entries.Add(new CkRangeAuditEntry(file, modelId, range, CkDependencyRangeRule.IsWithinOneMajor(range)));
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                unreadable?.Add($"{file}: {e.Message}");
            }
        }

        return entries;
    }

    private static IEnumerable<string> FindModelFiles(string root)
    {
        if (File.Exists(root))
        {
            yield return root;
            yield break;
        }

        if (!Directory.Exists(root))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            var candidate = Path.Combine(directory, "ckModel.yaml");
            if (File.Exists(candidate))
            {
                yield return candidate;
            }

            foreach (var child in Directory.EnumerateDirectories(directory).OrderByDescending(d => d, StringComparer.Ordinal))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(child)))
                {
                    pending.Push(child);
                }
            }
        }
    }
}

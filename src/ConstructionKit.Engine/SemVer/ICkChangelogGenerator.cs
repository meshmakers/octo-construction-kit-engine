using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Generates and updates <c>CHANGELOG.md</c> content from a classified model diff.
///     Pure string-to-string transformation — the caller owns all file IO.
/// </summary>
public interface ICkChangelogGenerator
{
    /// <summary>
    ///     Returns the changelog content with the section of the given version created or
    ///     replaced. Sections of other versions are never rewritten (byte-identical); a repeated
    ///     run for the same version and changes is idempotent. New sections are inserted on top
    ///     (newest first).
    /// </summary>
    /// <param name="existingContent">The current changelog content; null when no changelog exists yet</param>
    /// <param name="version">The version the section is generated for (the declared model version)</param>
    /// <param name="date">The date rendered in the section heading</param>
    /// <param name="requiredLevel">The minimum bump level required by the diff</param>
    /// <param name="classifiedChanges">The classified changes of the diff</param>
    /// <param name="note">Optional note rendered below the heading (e.g. for a first publication)</param>
    /// <returns>The updated changelog content</returns>
    string Generate(string? existingContent, CkVersion version, DateTime date, CkSemVerLevel requiredLevel,
        IReadOnlyList<CkClassifiedModelChange> classifiedChanges, string? note = null);
}

using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     A range-retaining dependency of a compiled model (CK v2, AB#5664): the declared range, preserved
///     verbatim, and the floor, the lowest acceptable version. An installed dependency satisfies it when its
///     version is inside <see cref="Range" /> and at or above <see cref="Floor" />.
/// </summary>
public class CkModelDependencyDto
{
    /// <summary>
    ///     The declared dependency range, e.g. <c>System-[2.4,3.0)</c>.
    /// </summary>
    public CkModelIdVersionRange Range { get; set; } = null!;

    /// <summary>
    ///     The floor as version string, e.g. <c>2.4.0</c>. Taken from the declared range lower bound, not from
    ///     the highest catalog version the model happened to be compiled against.
    /// </summary>
    public string Floor { get; set; } = null!;

    /// <summary>
    ///     CK v2 (AB#4472): the elements and members of this dependency the model uses, as sorted, de-duplicated,
    ///     major-qualified, version-less ids — element level for references (<c>System@2/Entity-1</c>, a base type,
    ///     an implemented interface, a reused attribute definition, a record or enum value type, an association role
    ///     or target) and member level for attribute paths into an inherited dependency type
    ///     (<c>System@2/Entity-1.Name</c>). <c>null</c> when not computed (models compiled before AB#4472).
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public List<string>? UsedSurface { get; set; }

    /// <summary>
    ///     CK v2 (AB#4472): <c>sha256:&lt;hex&gt;</c> over <see cref="UsedSurface" /> (one id per line, UTF-8). Lets a
    ///     consumer compare two surfaces without the list; the list stays authoritative.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? UsedSurfaceHash { get; set; }

    /// <summary>
    ///     <see cref="Floor" /> as <see cref="CkVersion" />.
    /// </summary>
    [JsonIgnore]
    [YamlIgnore]
    public CkVersion FloorVersion => new(Floor);

    /// <summary>
    ///     The range a resolver must satisfy: <see cref="Range" /> with its lower bound raised to
    ///     <see cref="Floor" /> (they are equal unless the floor was raised above the declared lower bound).
    /// </summary>
    public CkModelIdVersionRange GetEffectiveRange()
    {
        var range = Range.ModelVersionRange;
        var floor = FloorVersion;
        var min = range.MinVersion;
        var useFloor = min == null || floor.CompareTo(min.Value) > 0;
        if (!useFloor)
        {
            return Range;
        }

        var max = range.MaxVersion;
        var effective = max == null
            ? floor.ToString()
            : $"[{floor},{max}{(range.MaxInclusive ? "]" : ")")}";
        return new CkModelIdVersionRange(Range.Name, effective);
    }

    /// <summary>
    ///     True when <paramref name="installed" /> satisfies range and floor.
    /// </summary>
    public bool IsSatisfiedBy(CkModelId installed)
    {
        return installed.Name == Range.Name && !installed.IsMajorQualified
                                            && GetEffectiveRange().IsSatisfiedBy(installed);
    }

    /// <inheritdoc />
    public override string ToString() => $"{Range} (floor {Floor})";
}

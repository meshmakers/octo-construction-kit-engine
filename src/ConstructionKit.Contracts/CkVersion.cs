using Meshmakers.Octo.ConstructionKit.Contracts.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Represents a construction kit element version
/// </summary>
public readonly struct CkVersion : IComparable<CkVersion>, IEquatable<CkVersion>
{
    /// <summary>
    ///     Creates a new instance of <see cref="CkVersion" /> from its parts
    /// </summary>
    /// <param name="major">Major version, must not be negative</param>
    /// <param name="minor">Minor version, must not be negative</param>
    /// <param name="revision">Revision, must not be negative</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public CkVersion(int major, int minor, int revision)
    {
        if (major < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(major), "Major version must not be negative");
        }

        if (minor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minor), "Minor version must not be negative");
        }

        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), "Revision must not be negative");
        }

        Major = major;
        Minor = minor;
        Revision = revision;
    }

    /// <summary>
    ///     Creates a new instance of <see cref="CkVersion" />
    /// </summary>
    /// <param name="version">Version as string, e. g. 1.0.0</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public CkVersion(string version)
    {
        var versionParts = version.Split('.');
        if (versionParts.Length <= 0 || versionParts.Length > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Version must be in the format of 'major.minor.revision'");
        }
        if (!int.TryParse(versionParts[0], out _))
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Major version must be an integer");
        }

        Major = int.Parse(versionParts[0]);
        Minor = versionParts.Length >= 2 ? int.Parse(versionParts[1]) : 0;
        Revision = versionParts.Length >= 3 ? int.Parse(versionParts[2]) : 0;
    }


    /// <summary>
    ///     Creates a new instance of <see cref="CkVersion" /> from a string
    /// </summary>
    /// <param name="value">Version as string, e. g. 1.0.0</param>
    /// <returns></returns>
    public static implicit operator CkVersion(string value)
    {
        return new CkVersion(value);
    }

    /// <summary>
    ///     Returns the major version
    /// </summary>
    public int Major { get; }

    /// <summary>
    ///     Returns the minor version
    /// </summary>
    public int Minor { get; }

    /// <summary>
    ///     Returns the revision
    /// </summary>
    public int Revision { get; }

    /// <summary>
    ///     Compares this instance to another <see cref="CkVersion" /> instance
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public int CompareTo(CkVersion other)
    {
        if (Major != other.Major)
        {
            return Major.CompareTo(other.Major);
        }

        if (Minor != other.Minor)
        {
            return Minor.CompareTo(other.Minor);
        }

        if (Revision != other.Revision)
        {
            return Revision.CompareTo(other.Revision);
        }

        return 0;
    }
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public bool IsCompatible(CkVersion other)
    {
        if (Major != other.Major)
        {
            return false;
        }
        
        if (Minor < other.Minor)
        {
            return false;
        }
        
        return Revision >= other.Revision;
    }

    /// <summary>
    ///     Returns the version that results from bumping this version by exactly one step of the
    ///     given semantic version level. Bumping by <see cref="CkSemVerLevel.None" /> returns the
    ///     version unchanged.
    /// </summary>
    /// <param name="level">The semantic version level to bump by</param>
    /// <returns>The bumped version</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for unknown levels</exception>
    public CkVersion Bump(CkSemVerLevel level)
    {
        return level switch
        {
            CkSemVerLevel.None => this,
            CkSemVerLevel.Patch => new CkVersion(Major, Minor, Revision + 1),
            CkSemVerLevel.Minor => new CkVersion(Major, Minor + 1, 0),
            CkSemVerLevel.Major => new CkVersion(Major + 1, 0, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown semantic version level")
        };
    }

    /// <summary>
    ///     Returns true when this version is at least one bump of the given level above the
    ///     baseline version. Higher versions than the exact bump are also accepted
    ///     (minimum level semantics).
    /// </summary>
    /// <param name="baseline">The baseline version the bump is measured against</param>
    /// <param name="level">The required semantic version level</param>
    /// <returns>True when this version satisfies the minimum bump</returns>
    public bool IsAtLeastBumpOf(CkVersion baseline, CkSemVerLevel level)
    {
        return CompareTo(baseline.Bump(level)) >= 0;
    }

    /// <inheritdoc />
    public bool Equals(CkVersion other)
    {
        return Major == other.Major && Minor == other.Minor && Revision == other.Revision;
    }

    /// <summary>
    ///     Returns a string representation of the value.
    /// </summary>
    /// <returns>A string representation of the value.</returns>
    public override string ToString()
    {
        return $"{Major}.{Minor}.{Revision}";
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is CkVersion other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 19;
            hash = hash * 25 + Major.GetHashCode();
            hash = hash * 25 + Minor.GetHashCode();
            hash = hash * 25 + Revision.GetHashCode();
            return hash;
        }
    }

    /// <summary>
    ///     Compares two <see cref="CkVersion" /> values for equality.
    /// </summary>
    /// <param name="p1"></param>
    /// <param name="p2"></param>
    /// <returns></returns>
    public static bool operator ==(CkVersion p1, CkVersion p2)
    {
        return p1.Equals(p2);
    }

    /// <summary>
    ///     Compares two <see cref="CkVersion" /> values for inequality.
    /// </summary>
    /// <param name="p1"></param>
    /// <param name="p2"></param>
    /// <returns></returns>
    public static bool operator !=(CkVersion p1, CkVersion p2)
    {
        return !p1.Equals(p2);
    }
}
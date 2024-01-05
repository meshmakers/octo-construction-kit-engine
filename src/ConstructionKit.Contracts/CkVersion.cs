namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Represents a construction kit element version
/// </summary>
public readonly struct CkVersion : IComparable<CkVersion>, IEquatable<CkVersion>
{
    /// <summary>
    ///     Creates a new instance of <see cref="CkVersion" />
    /// </summary>
    /// <param name="version">Version as string, e. g. 1.0.0</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public CkVersion(string version)
    {
        var versionParts = version.Split('.');
        if (versionParts.Length != 3)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Version must be in the format of 'major.minor.revision'");
        }

        Major = int.Parse(versionParts[0]);
        Minor = int.Parse(versionParts[1]);
        Revision = int.Parse(versionParts[2]);
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
        if (obj == null)
        {
            return false;
        }

        var other = (CkVersion)obj;

        return Major == other.Major && Minor == other.Minor && Revision == other.Revision;
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
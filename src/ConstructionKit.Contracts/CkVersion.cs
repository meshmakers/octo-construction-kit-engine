namespace Meshmakers.Octo.ConstructionKit.Contracts;

public readonly struct CkVersion : IComparable<CkVersion>, IEquatable<CkVersion>
{
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
    
    
    public static implicit operator CkVersion(string value)
    {
        return new CkVersion(value);
    }
    
    public int Major { get; }
    public int Minor { get; }
    public int Revision { get; }
    
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
    
    public override bool Equals(object? obj)
    {
        if (obj == null)
        {
            return false;
        }
        
        var other = (CkVersion)obj;
        
        return Major == other.Major && Minor == other.Minor && Revision == other.Revision;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 19;
            hash = hash * 25 + Major.GetHashCode();
            hash = hash * 25 + Minor.GetHashCode();
            hash = hash * 25 + Revision.GetHashCode();
            return hash;
        }
    }
    
    public static bool operator ==(CkVersion p1, CkVersion p2)
    {
        return p1.Equals(p2);
    }

    public static bool operator !=(CkVersion p1, CkVersion p2)
    {
        return !p1.Equals(p2);
    }
}
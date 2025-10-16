namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Represents a construction kit dependency version range
/// </summary>
/// <remarks>
/// <para>
/// Version range syntax:
/// </para>
/// <para>
/// <b>Simple version strings (without brackets):</b>
/// </para>
/// <list type="bullet">
/// <item>1.0 or 1.0.0 - Creates an open-ended minimum version range, inclusive (x &gt;= 1.0.0)</item>
/// <item>This means "1.0.0" includes ALL versions from 1.0.0 onwards (1.0.0, 1.0.1, 1.1.0, 2.0.0, etc.)</item>
/// </list>
/// <para>
/// <b>Exact version matching:</b>
/// </para>
/// <list type="bullet">
/// <item>[1.0] or [1.0.0] - Exact version match only (x == 1.0.0)</item>
/// <item>This matches ONLY the specified version, nothing else</item>
/// </list>
/// <para>
/// <b>Range specifications:</b>
/// </para>
/// <list type="bullet">
/// <item>[1.0,) - Minimum version, inclusive (x &gt;= 1.0.0) - same as simple "1.0"</item>
/// <item>(1.0,) - Minimum version, exclusive (x &gt; 1.0.0)</item>
/// <item>(,1.0] - Maximum version, inclusive (x &lt;= 1.0.0)</item>
/// <item>(,1.0) - Maximum version, exclusive (x &lt; 1.0.0)</item>
/// <item>[1.0,2.0] - Bounded range, inclusive (1.0.0 &lt;= x &lt;= 2.0.0)</item>
/// <item>(1.0,2.0) - Bounded range, exclusive (1.0.0 &lt; x &lt; 2.0.0)</item>
/// <item>[1.0,2.0) - Mixed inclusive minimum and exclusive maximum (1.0.0 &lt;= x &lt; 2.0.0)</item>
/// </list>
/// <para>
/// <b>Important note on overlapping:</b>
/// Two ranges overlap if there exists at least one version that satisfies both ranges.
/// For example, "1.0.0" (which means &gt;= 1.0.0) and "1.0.1" (which means &gt;= 1.0.1) DO overlap
/// because all versions &gt;= 1.0.1 satisfy both ranges.
/// </para>
/// </remarks>
public readonly struct CkVersionRange : IEquatable<CkVersionRange>, IComparable<CkVersionRange>
{
    private readonly CkVersion? _minVersion;
    private readonly CkVersion? _maxVersion;
    private readonly bool _minInclusive;
    private readonly bool _maxInclusive;
    private readonly string _originalRange;

    /// <summary>
    ///     Creates a new instance of <see cref="CkVersionRange" />
    /// </summary>
    /// <param name="versionRange">Version range as string. Examples:
    /// <list type="bullet">
    /// <item>"1.0.0" - Creates range [1.0.0,) meaning version &gt;= 1.0.0</item>
    /// <item>"[1.0.0]" - Exact version 1.0.0 only</item>
    /// <item>"[1.0.0,2.0.0)" - Range from 1.0.0 inclusive to 2.0.0 exclusive</item>
    /// </list>
    /// </param>
    /// <exception cref="ArgumentException">Thrown when the version range format is invalid</exception>
    public CkVersionRange(string versionRange)
    {
        if (string.IsNullOrWhiteSpace(versionRange))
        {
            throw new ArgumentException("Version range cannot be null or empty", nameof(versionRange));
        }

        _originalRange = versionRange.Trim();

        // Handle simple version format (e.g., "1.0.0" means >= 1.0.0)
        if (!_originalRange.Contains(',') && !_originalRange.StartsWith("(") && !_originalRange.StartsWith("["))
        {
            _minVersion = ParseVersion(_originalRange);
            _minInclusive = true;
            _maxVersion = null;
            _maxInclusive = false;
            return;
        }

        // Handle exact version match [1.0.0]
        if (_originalRange.StartsWith("[") && _originalRange.EndsWith("]") && !_originalRange.Contains(','))
        {
            var versionStr = _originalRange.Substring(1, _originalRange.Length - 2).Trim();
            _minVersion = ParseVersion(versionStr);
            _maxVersion = _minVersion;
            _minInclusive = true;
            _maxInclusive = true;
            return;
        }

        // Handle range formats with brackets/parentheses
        if (!(_originalRange.StartsWith("(") || _originalRange.StartsWith("[")))
        {
            throw new ArgumentException($"Invalid version range format: {versionRange}. Range must start with '(' or '['.", nameof(versionRange));
        }

        if (!(_originalRange.EndsWith(")") || _originalRange.EndsWith("]")))
        {
            throw new ArgumentException($"Invalid version range format: {versionRange}. Range must end with ')' or ']'.", nameof(versionRange));
        }

        _minInclusive = _originalRange[0] == '[';
        // ReSharper disable once UseIndexFromEndExpression
        _maxInclusive = _originalRange[_originalRange.Length - 1] == ']';

        // Remove brackets and split by comma
        var innerRange = _originalRange.Substring(1, _originalRange.Length - 2);

        // Special case: (1.0) is invalid
        if (!innerRange.Contains(','))
        {
            throw new ArgumentException($"Invalid version range format: {versionRange}. Single version in parentheses is not allowed.", nameof(versionRange));
        }

        var parts = innerRange.Split(',');
        if (parts.Length != 2)
        {
            throw new ArgumentException($"Invalid version range format: {versionRange}. Range must contain exactly one comma.", nameof(versionRange));
        }

        var minPart = parts[0].Trim();
        var maxPart = parts[1].Trim();

        // Parse minimum version
        _minVersion = string.IsNullOrEmpty(minPart) ? (CkVersion?)null : ParseVersion(minPart);

        // Parse maximum version
        _maxVersion = string.IsNullOrEmpty(maxPart) ? (CkVersion?)null : ParseVersion(maxPart);

        // Validate that we have at least one bound
        if (_minVersion == null && _maxVersion == null)
        {
            throw new ArgumentException($"Invalid version range format: {versionRange}. At least one bound must be specified.", nameof(versionRange));
        }

        // Validate that min <= max if both are specified
        if (_minVersion != null && _maxVersion != null && _minVersion.Value.CompareTo(_maxVersion.Value) > 0)
        {
            throw new ArgumentException($"Invalid version range: minimum version {_minVersion} is greater than maximum version {_maxVersion}.", nameof(versionRange));
        }
    }

    private static CkVersion ParseVersion(string versionString)
    {
        // Support both formats: "1.0" and "1.0.0"
        var parts = versionString.Split('.');
        if (parts.Length == 2)
        {
            // Append .0 for revision if not provided
            versionString = $"{versionString}.0";
        }
        else if (parts.Length != 3)
        {
            throw new ArgumentException($"Invalid version format: {versionString}. Version must be in format 'major.minor' or 'major.minor.revision'.");
        }

        return new CkVersion(versionString);
    }


    /// <summary>
    ///     Creates a new instance of <see cref="CkVersionRange" /> from a string
    /// </summary>
    /// <param name="value">Version range as string</param>
    /// <returns></returns>
    public static implicit operator CkVersionRange(string value)
    {
        return new CkVersionRange(value);
    }

    /// <summary>
    ///     Gets the minimum version (if specified)
    /// </summary>
    public CkVersion? MinVersion => _minVersion;

    /// <summary>
    ///     Gets the maximum version (if specified)
    /// </summary>
    public CkVersion? MaxVersion => _maxVersion;

    /// <summary>
    ///     Gets whether the minimum version is inclusive
    /// </summary>
    public bool MinInclusive => _minInclusive;

    /// <summary>
    ///     Gets whether the maximum version is inclusive
    /// </summary>
    public bool MaxInclusive => _maxInclusive;

    /// <summary>
    ///     Checks if a version satisfies this version range
    /// </summary>
    /// <param name="version">The version to check</param>
    /// <returns>True if the version is within the range, false otherwise</returns>
    public bool IsSatisfiedBy(CkVersion version)
    {
        // Check minimum bound
        if (_minVersion != null)
        {
            var comparison = version.CompareTo(_minVersion.Value);
            if (_minInclusive)
            {
                if (comparison < 0) return false;
            }
            else
            {
                if (comparison <= 0) return false;
            }
        }

        // Check maximum bound
        if (_maxVersion != null)
        {
            var comparison = version.CompareTo(_maxVersion.Value);
            if (_maxInclusive)
            {
                if (comparison > 0) return false;
            }
            else
            {
                if (comparison >= 0) return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Checks if another version range overlaps with this one
    /// </summary>
    /// <param name="other">The other version range</param>
    /// <returns>True if there exists at least one version that satisfies both ranges, false otherwise</returns>
    /// <remarks>
    /// Two ranges overlap if there is any version that would be accepted by both ranges.
    /// For example:
    /// <list type="bullet">
    /// <item>"1.0.0" (meaning &gt;= 1.0.0) and "1.0.1" (meaning &gt;= 1.0.1) DO overlap because both ranges share versions like 1.0.1, 1.0.2, 1.1.0, etc. Note: "1.0.1" does NOT include 1.0.0, but the ranges still overlap via their shared versions.</item>
    /// <item>"[1.0.0]" (exactly 1.0.0) and "[1.0.1]" (exactly 1.0.1) do NOT overlap - no version satisfies both</item>
    /// <item>"[1.0.0,2.0.0]" and "[1.5.0,3.0.0]" DO overlap - versions 1.5.0 to 2.0.0 satisfy both</item>
    /// <item>"[1.0.0,2.0.0)" and "[2.0.0,3.0.0]" do NOT overlap - no version satisfies both (2.0.0 is excluded from first, included in second)</item>
    /// </list>
    /// </remarks>
    public bool Overlaps(CkVersionRange other)
    {
        // Check if this range's max is less than other's min
        if (_maxVersion != null && other._minVersion != null)
        {
            var comparison = _maxVersion.Value.CompareTo(other._minVersion.Value);
            switch (comparison)
            {
                case < 0:
                case 0 when !_maxInclusive || !other._minInclusive:
                    return false;
            }
        }

        // Check if other range's max is less than this min
        if (other._maxVersion != null && _minVersion != null)
        {
            var comparison = other._maxVersion.Value.CompareTo(_minVersion.Value);
            switch (comparison)
            {
                case < 0:
                case 0 when !other._maxInclusive || !_minInclusive:
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Checks if a version is compatible with this version range
    /// </summary>
    /// <param name="other">The version to check compatibility with</param>
    /// <returns>True if the version is compatible, false otherwise</returns>
    /// <remarks>This is an alias for IsSatisfiedBy for backward compatibility</remarks>
    public bool IsCompatible(CkVersion other)
    {
        return IsSatisfiedBy(other);
    }

    /// <summary>
    ///     Compares this version range to another version range
    /// </summary>
    /// <param name="other">The other version range to compare to</param>
    /// <returns>
    ///     A value that indicates the relative order of the ranges:
    ///     Less than zero: This range is considered "less than" the other range
    ///     Zero: This range is equivalent to the other range
    ///     Greater than zero: This range is considered "greater than" the other range
    /// </returns>
    /// <remarks>
    /// Comparison logic:
    /// 1. First compare minimum versions (null is treated as "earliest")
    /// 2. If minimum versions are equal, compare minimum inclusivity (inclusive &lt; exclusive)
    /// 3. If still equal, compare maximum versions (null is treated as "latest")
    /// 4. If maximum versions are equal, compare maximum inclusivity (exclusive &lt; inclusive)
    /// </remarks>
    public int CompareTo(CkVersionRange other)
    {

        // Compare minimum versions first
        if (_minVersion == null && other._minVersion == null)
        {
            // Both have no minimum - continue to next comparison
        }
        else if (_minVersion == null)
        {
            // This has no minimum (earlier), other has minimum
            return -1;
        }
        else if (other._minVersion == null)
        {
            // Other has no minimum (earlier), this has a minimum
            return 1;
        }
        else
        {
            // Both have minimum versions - compare them
            var minComparison = _minVersion.Value.CompareTo(other._minVersion.Value);
            if (minComparison != 0)
                return minComparison;

            // Minimum versions are equal - compare inclusivity
            // Inclusive minimum is "less than" exclusive minimum (more permissive)
            if (_minInclusive != other._minInclusive)
            {
                return _minInclusive ? -1 : 1;
            }
        }

        // Compare maximum versions
        if (_maxVersion == null && other._maxVersion == null)
        {
            // Both have no maximum - continue to next comparison
        }
        else if (_maxVersion == null)
        {
            // This has no maximum (later), other has maximum
            return 1;
        }
        else if (other._maxVersion == null)
        {
            // Other has no maximum (later), this has maximum
            return -1;
        }
        else
        {
            // Both have maximum versions - compare them
            var maxComparison = _maxVersion.Value.CompareTo(other._maxVersion.Value);
            if (maxComparison != 0)
                return maxComparison;

            // Maximum versions are equal - compare inclusivity
            // Exclusive maximum is "less than" inclusive maximum (more restrictive)
            if (_maxInclusive != other._maxInclusive)
            {
                return _maxInclusive ? 1 : -1;
            }
        }

        // All components are equal
        return 0;
    }

    /// <inheritdoc />
    public bool Equals(CkVersionRange other)
    {
        return _minVersion == other._minVersion &&
               _maxVersion == other._maxVersion &&
               _minInclusive == other._minInclusive &&
               _maxInclusive == other._maxInclusive;
    }

    /// <summary>
    ///     Returns a string representation of the version range.
    /// </summary>
    /// <returns>A string representation of the version range.</returns>
    public override string ToString()
    {
        return _originalRange;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is CkVersionRange other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 19;
            hash = hash * 31 + (_minVersion?.GetHashCode() ?? 0);
            hash = hash * 31 + (_maxVersion?.GetHashCode() ?? 0);
            hash = hash * 31 + _minInclusive.GetHashCode();
            hash = hash * 31 + _maxInclusive.GetHashCode();
            return hash;
        }
    }

    /// <summary>
    ///     Compares two <see cref="CkVersionRange" /> values for equality.
    /// </summary>
    /// <param name="p1"></param>
    /// <param name="p2"></param>
    /// <returns></returns>
    public static bool operator ==(CkVersionRange p1, CkVersionRange p2)
    {
        return p1.Equals(p2);
    }

    /// <summary>
    ///     Compares two <see cref="CkVersionRange" /> values for inequality.
    /// </summary>
    /// <param name="p1"></param>
    /// <param name="p2"></param>
    /// <returns></returns>
    public static bool operator !=(CkVersionRange p1, CkVersionRange p2)
    {
        return !p1.Equals(p2);
    }
}
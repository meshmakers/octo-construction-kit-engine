using System.Diagnostics;
using System.Text.Json.Serialization;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Represents a versioned construction kit model id
/// </summary>
/// <remarks>
/// We use a dash ("-") to separate the model id and the version.
/// The version number of a model allows to manage versioning of models, it is allowed to have
/// different elements (types, records, etc.) with the same name in different versions of a model.
/// </remarks>
[DebuggerDisplay("{" + nameof(Name) + "}-{" + nameof(Version) + "}")]
[JsonConverter(typeof(CkModelIdConverter))]
public sealed record CkModelId : IComparable<CkModelId>, ICkElementId
{
    private readonly string? _modelId;

    /// <summary>
    ///     Creates a new <see cref="CkModelId" /> from the given <paramref name="ckModelId" />.
    /// </summary>
    /// <param name="ckModelId"></param>
    public CkModelId(string ckModelId)
    {
        var versionIndex = ckModelId.IndexOf("-", StringComparison.Ordinal);
        var majorIndex = ckModelId.IndexOf(MajorQualifierSeparator);
        if (majorIndex > 0 && versionIndex < 0
                           && int.TryParse(ckModelId.Substring(majorIndex + 1), out var major) && major >= 0)
        {
            // AB#5664: major-qualified, model-version-less reference ("System@2").
            _modelId = ckModelId.Substring(0, majorIndex);
            Version = new CkVersion(major, 0, 0);
            IsMajorQualified = true;
        }
        else if (versionIndex > 0)
        {
            _modelId = ckModelId.Substring(0, versionIndex);
            Version = ckModelId.Substring(versionIndex + 1);
        }
        else
        {
            _modelId = ckModelId;
            Version = "1.0.0";
        }
    }

    /// <summary>
    ///     Creates a new <see cref="CkModelId" /> from the given <paramref name="modelId" /> and <paramref name="version" />.
    /// </summary>
    /// <param name="modelId"></param>
    /// <param name="version"></param>
    public CkModelId(string modelId, string version)
    {
        _modelId = modelId;
        Version = version;
    }

    /// <summary>
    ///     Creates a new <see cref="CkModelId" /> from the given <paramref name="modelId" /> and <paramref name="version" />.
    /// </summary>
    /// <param name="modelId"></param>
    /// <param name="version"></param>
    public CkModelId(string modelId, CkVersion version)
    {
        _modelId = modelId;
        Version = version;
    }

    /// <summary>
    ///     Separator of a major-qualified model reference, e.g. <c>System@2</c> (AB#5664).
    /// </summary>
    public const char MajorQualifierSeparator = '@';

    /// <summary>
    ///     Creates a major-qualified, model-version-less model reference (<c>System@2</c>). Compiled models
    ///     with range retention (CK v2, AB#5664) store references into their dependencies in this form; they
    ///     are bound to the installed version of that major when the model is resolved.
    /// </summary>
    /// <param name="modelName">Name of the model, e.g. "System".</param>
    /// <param name="major">Major version.</param>
    public static CkModelId MajorQualified(string modelName, int major)
    {
        return new CkModelId($"{modelName}{MajorQualifierSeparator}{major}");
    }

    /// <summary>
    ///     True when this id is a major-qualified reference (<c>System@2</c>) instead of a concrete model
    ///     version. <see cref="Version" /> then carries only the major (<c>2.0.0</c>).
    /// </summary>
    public bool IsMajorQualified { get; }

    /// <summary>
    ///     Returns the major-qualified form of this id (<c>System-2.4.0</c> → <c>System@2</c>).
    /// </summary>
    public CkModelId ToMajorQualified()
    {
        return IsMajorQualified ? this : MajorQualified(Name, Version.Major);
    }

    /// <summary>
    ///     Creates a new <see cref="CkModelId" /> from the given <paramref name="value" />.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static implicit operator CkModelId(string value)
    {
        return new CkModelId(value);
    }

    /// <summary>
    ///     Returns the id of the model, e. g. "System"
    /// </summary>
    public string Name => _modelId ?? "";

    /// <summary>
    ///     Returns the version of the model, e. g. "1.0.0"
    /// </summary>
    public CkVersion Version { get; }

    /// <summary>
    ///     Returns the full name of the model, e. g. "System-1.0.0"
    /// </summary>
    // ReSharper disable once MemberCanBePrivate.Global
    public string FullName => IsEmpty ? "" : Name.StartsWith("$") ? Name
        : IsMajorQualified ? $"{Name}{MajorQualifierSeparator}{Version.Major}" : $"{Name}-{Version}";

    /// <inheritdoc />
    public string SemanticVersionedFullName
    {
        get
        {
            if (IsEmpty)
            {
                return "";
            }

            var s = Name;
            if (Version.Major > 1)
            {
                s += $"-{Version.Major}";
            }

            return s;
        }
    }

    /// <inheritdoc />
    public bool IsEmpty => string.IsNullOrWhiteSpace(Name);

    /// <inheritdoc />
    public TypeCode GetTypeCode()
    {
        return TypeCode.Object;
    }

    /// <inheritdoc />
    public bool ToBoolean(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public byte ToByte(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public char ToChar(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public DateTime ToDateTime(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public decimal ToDecimal(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public double ToDouble(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public short ToInt16(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public int ToInt32(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public long ToInt64(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public sbyte ToSByte(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public float ToSingle(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public string ToString(IFormatProvider? provider)
    {
        return FullName;
    }

    /// <inheritdoc />
    public object ToType(Type conversionType, IFormatProvider? provider)
    {
        switch (Type.GetTypeCode(conversionType))
        {
            case TypeCode.String:
                return ToString(provider);
            case TypeCode.Object:
                if (conversionType == typeof(object) || conversionType == typeof(CkModelId))
                {
                    return this;
                }

                break;
        }

        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public ushort ToUInt16(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public uint ToUInt32(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public ulong ToUInt64(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    /// <inheritdoc />
    public int CompareTo(CkModelId? other)
    {
        if (other == null)
        {
            return 1;
        }
        var result = string.Compare(Name, other.Name, StringComparison.Ordinal);
        if (result != 0)
        {
            return result;
        }

        result = Version.CompareTo(other.Version);
        return result != 0 ? result : IsMajorQualified.CompareTo(other.IsMajorQualified);
    }

    /// <inheritdoc />
    public bool Equals(CkModelId? other)
    {
        return other is not null && Name == other.Name && Version == other.Version
               && IsMajorQualified == other.IsMajorQualified;
    }

    /// <summary>
    ///     Returns a string representation of the value.
    /// </summary>
    /// <returns>A string representation of the value.</returns>
    public override string ToString()
    {
        return FullName;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 52;
            hash = hash * 12 + Name.GetHashCode();
            return hash;
        }
    }

    /// <summary>
    /// Converts to a version range
    /// </summary>
    /// <returns></returns>
    public CkModelIdVersionRange ToVersionRange()
    {
        return IsMajorQualified
            ? new CkModelIdVersionRange(Name, $"[{Version.Major}.0,{Version.Major + 1}.0)")
            : new CkModelIdVersionRange(Name, $"[{Version.ToString()}]");
    }
}
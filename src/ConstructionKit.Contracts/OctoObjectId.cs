using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text.Json.Serialization;
using Meshmakers.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Represents an Octo object id
/// </summary>
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
[JsonConverter(typeof(OctoObjectIdConverter))]
public readonly struct OctoObjectId : IComparable<OctoObjectId>, IEquatable<OctoObjectId>, IConvertible
{
    // private static fields
    private static readonly long Random = CalculateRandomValue();
    private static int _staticIncrement = new Random().Next();

    // private fields
    private readonly int _a;
    private readonly int _b;
    private readonly int _c;

    // constructors
    /// <summary>
    ///     Initializes a new instance of the ObjectId class.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    public OctoObjectId(byte[] bytes)
    {
        if (bytes == null)
        {
            throw new ArgumentNullException(nameof(bytes));
        }

        if (bytes.Length != 12)
        {
            throw new ArgumentException("Byte array must be 12 bytes long", nameof(bytes));
        }

        FromByteArray(bytes, 0, out _a, out _b, out _c);
    }

    /// <summary>
    ///     Initializes a new instance of the ObjectId class.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="index">The index into the byte array where the ObjectId starts.</param>
    internal OctoObjectId(byte[] bytes, int index)
    {
        FromByteArray(bytes, index, out _a, out _b, out _c);
    }

    /// <summary>
    ///     Initializes a new instance of the ObjectId class.
    /// </summary>
    /// <param name="value">The value.</param>
    public OctoObjectId(string value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (value.Length != 24)
        {
            throw new FormatException(
                $"'{value}' is not a valid 24 digit hex string (got {value.Length} characters).");
        }

        var bytes = value.ParseHexString();
        FromByteArray(bytes, 0, out _a, out _b, out _c);
    }

    private OctoObjectId(int a, int b, int c)
    {
        _a = a;
        _b = b;
        _c = c;
    }

    // public static properties
    /// <summary>
    ///     Gets an instance of ObjectId where the value is empty.
    /// </summary>
    public static OctoObjectId Empty { get; } = default;

    // public properties
    /// <summary>
    ///     Gets the timestamp.
    /// </summary>
    public int Timestamp => _a;

    /// <summary>
    ///     Gets the creation time (derived from the timestamp).
    /// </summary>
    public DateTime CreationTime => CkModelStatics.UnixEpoch.AddSeconds((uint)Timestamp);

    // public operators
    /// <summary>
    ///     Compares two ObjectIds.
    /// </summary>
    /// <param name="lhs">The first ObjectId.</param>
    /// <param name="rhs">The other ObjectId</param>
    /// <returns>True if the first ObjectId is less than the second ObjectId.</returns>
    public static bool operator <(OctoObjectId lhs, OctoObjectId rhs)
    {
        return lhs.CompareTo(rhs) < 0;
    }

    /// <summary>
    ///     Compares two ObjectIds.
    /// </summary>
    /// <param name="lhs">The first ObjectId.</param>
    /// <param name="rhs">The other ObjectId</param>
    /// <returns>True if the first ObjectId is less than or equal to the second ObjectId.</returns>
    public static bool operator <=(OctoObjectId lhs, OctoObjectId rhs)
    {
        return lhs.CompareTo(rhs) <= 0;
    }

    /// <summary>
    ///     Compares two ObjectIds.
    /// </summary>
    /// <param name="lhs">The first ObjectId.</param>
    /// <param name="rhs">The other ObjectId.</param>
    /// <returns>True if the two ObjectIds are equal.</returns>
    public static bool operator ==(OctoObjectId lhs, OctoObjectId rhs)
    {
        return lhs.Equals(rhs);
    }

    /// <summary>
    ///     Compares two ObjectIds.
    /// </summary>
    /// <param name="lhs">The first ObjectId.</param>
    /// <param name="rhs">The other ObjectId.</param>
    /// <returns>True if the two ObjectIds are not equal.</returns>
    public static bool operator !=(OctoObjectId lhs, OctoObjectId rhs)
    {
        return !(lhs == rhs);
    }

    /// <summary>
    ///     Compares two ObjectIds.
    /// </summary>
    /// <param name="lhs">The first ObjectId.</param>
    /// <param name="rhs">The other ObjectId</param>
    /// <returns>True if the first ObjectId is greater than or equal to the second ObjectId.</returns>
    public static bool operator >=(OctoObjectId lhs, OctoObjectId rhs)
    {
        return lhs.CompareTo(rhs) >= 0;
    }

    /// <summary>
    ///     Compares two ObjectIds.
    /// </summary>
    /// <param name="lhs">The first ObjectId.</param>
    /// <param name="rhs">The other ObjectId</param>
    /// <returns>True if the first ObjectId is greater than the second ObjectId.</returns>
    public static bool operator >(OctoObjectId lhs, OctoObjectId rhs)
    {
        return lhs.CompareTo(rhs) > 0;
    }

    // public static methods
    /// <summary>
    ///     Generates a new ObjectId with a unique value.
    /// </summary>
    /// <returns>An ObjectId.</returns>
    public static OctoObjectId GenerateNewId()
    {
        return GenerateNewId(GetTimestampFromDateTime(DateTime.UtcNow));
    }

    /// <summary>
    ///     Generates a new ObjectId with a unique value (with the timestamp component based on a given DateTime).
    /// </summary>
    /// <param name="timestamp">The timestamp component (expressed as a DateTime).</param>
    /// <returns>An ObjectId.</returns>
    public static OctoObjectId GenerateNewId(DateTime timestamp)
    {
        return GenerateNewId(GetTimestampFromDateTime(timestamp));
    }

    /// <summary>
    ///     Generates a new ObjectId with a unique value (with the given timestamp).
    /// </summary>
    /// <param name="timestamp">The timestamp component.</param>
    /// <returns>An ObjectId.</returns>
    public static OctoObjectId GenerateNewId(int timestamp)
    {
        var increment = Interlocked.Increment(ref _staticIncrement) & 0x00ffffff; // only use low order 3 bytes
        return Create(timestamp, Random, increment);
    }


    /// <summary>
    ///     Parses a string and creates a new ObjectId.
    /// </summary>
    /// <param name="s">The string value.</param>
    /// <returns>A ObjectId.</returns>
    public static OctoObjectId Parse(string s)
    {
        if (s == null)
        {
            throw new ArgumentNullException(nameof(s));
        }

        if (TryParse(s, out var objectId))
        {
            return objectId;
        }

        var message = $"'{s}' is not a valid 24 digit hex string.";
        throw new FormatException(message);
    }

    /// <summary>
    ///     Tries to parse a string and create a new ObjectId.
    /// </summary>
    /// <param name="s">The string value.</param>
    /// <param name="objectId">The new ObjectId.</param>
    /// <returns>True if the string was parsed successfully.</returns>
    public static bool TryParse(string s, out OctoObjectId objectId)
    {
        // don't throw ArgumentNullException if s is null
        if (s.Length == 24)
        {
            if (s.TryParseHexString(out var bytes) && bytes != null)
            {
                objectId = new OctoObjectId(bytes);
                return true;
            }
        }

        objectId = default;
        return false;
    }

    // private static methods
    private static long CalculateRandomValue()
    {
        var seed = (int)DateTime.UtcNow.Ticks ^ GetMachineHash() ^ GetPid();
        var random = new Random(seed);
        var high = random.Next();
        var low = random.Next();
        var combined = (long)(((ulong)(uint)high << 32) | (uint)low);
        return combined & 0xffffffffff; // low order 5 bytes
    }

    private static OctoObjectId Create(int timestamp, long random, int increment)
    {
        if (random < 0 || random > 0xffffffffff)
        {
            throw new ArgumentOutOfRangeException(nameof(random),
                "The random value must be between 0 and 1099511627775 (it must fit in 5 bytes).");
        }

        if (increment < 0 || increment > 0xffffff)
        {
            throw new ArgumentOutOfRangeException(nameof(increment),
                "The increment value must be between 0 and 16777215 (it must fit in 3 bytes).");
        }

        var a = timestamp;
        var b = (int)(random >> 8); // first 4 bytes of random
        var c = (int)(random << 24) | increment; // 5th byte of random and 3 byte increment
        return new OctoObjectId(a, b, c);
    }

    /// <summary>
    ///     Gets the current process id.  This method exists because of how CAS operates on the call stack, checking
    ///     for permissions before executing the method.  Hence, if we inlined this call, the calling method would not execute
    ///     before throwing an exception requiring the try/catch at an even higher level that we don't necessarily control.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int GetCurrentProcessId()
    {
        return Process.GetCurrentProcess().Id;
    }

    private static int GetMachineHash()
    {
        // use instead of Dns.HostName so it will work offline
        var machineName = GetMachineName();
        return 0x00ffffff & machineName.GetHashCode(); // use first 3 bytes of hash
    }

    private static string GetMachineName()
    {
        return Environment.MachineName;
    }

    private static short GetPid()
    {
        try
        {
            return (short)GetCurrentProcessId(); // use low order two bytes only
        }
        catch (SecurityException)
        {
            return 0;
        }
    }

    private static int GetTimestampFromDateTime(DateTime timestamp)
    {
        var secondsSinceEpoch =
            (long)Math.Floor((timestamp.ToUniversalTime() - CkModelStatics.UnixEpoch).TotalSeconds);
        if (secondsSinceEpoch < uint.MinValue || secondsSinceEpoch > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        }

        return (int)(uint)secondsSinceEpoch;
    }

    private static void FromByteArray(byte[] bytes, int offset, out int a, out int b, out int c)
    {
        a = (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        b = (bytes[offset + 4] << 24) | (bytes[offset + 5] << 16) | (bytes[offset + 6] << 8) | bytes[offset + 7];
        c = (bytes[offset + 8] << 24) | (bytes[offset + 9] << 16) | (bytes[offset + 10] << 8) | bytes[offset + 11];
    }

    // public methods
    /// <summary>
    ///     Compares this ObjectId to another ObjectId.
    /// </summary>
    /// <param name="other">The other ObjectId.</param>
    /// <returns>
    ///     A 32-bit signed integer that indicates whether this ObjectId is less than, equal to, or greater than the
    ///     other.
    /// </returns>
    public int CompareTo(OctoObjectId other)
    {
        var result = ((uint)_a).CompareTo((uint)other._a);
        if (result != 0)
        {
            return result;
        }

        result = ((uint)_b).CompareTo((uint)other._b);
        if (result != 0)
        {
            return result;
        }

        return ((uint)_c).CompareTo((uint)other._c);
    }

    /// <summary>
    ///     Compares this ObjectId to another ObjectId.
    /// </summary>
    /// <param name="rhs">The other ObjectId.</param>
    /// <returns>True if the two ObjectIds are equal.</returns>
    public bool Equals(OctoObjectId rhs)
    {
        return
            _a == rhs._a &&
            _b == rhs._b &&
            _c == rhs._c;
    }

    /// <summary>
    ///     Compares this ObjectId to another object.
    /// </summary>
    /// <param name="obj">The other object.</param>
    /// <returns>True if the other object is an ObjectId and equal to this one.</returns>
    public override bool Equals(object? obj)
    {
        if (obj is OctoObjectId id)
        {
            return Equals(id);
        }

        return false;
    }

    /// <summary>
    ///     Gets the hash code.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = 17;
        hash = 37 * hash + _a.GetHashCode();
        hash = 37 * hash + _b.GetHashCode();
        hash = 37 * hash + _c.GetHashCode();
        return hash;
    }

    /// <summary>
    ///     Converts the ObjectId to a byte array.
    /// </summary>
    /// <returns>A byte array.</returns>
    public byte[] ToByteArray()
    {
        var bytes = new byte[12];
        ToByteArray(bytes, 0);
        return bytes;
    }

    /// <summary>
    ///     Converts the ObjectId to a byte array.
    /// </summary>
    /// <param name="destination">The destination.</param>
    /// <param name="offset">The offset.</param>
    public void ToByteArray(byte[] destination, int offset)
    {
        if (destination == null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        if (offset + 12 > destination.Length)
        {
            throw new ArgumentException("Not enough room in destination buffer.", nameof(offset));
        }

        destination[offset + 0] = (byte)(_a >> 24);
        destination[offset + 1] = (byte)(_a >> 16);
        destination[offset + 2] = (byte)(_a >> 8);
        destination[offset + 3] = (byte)_a;
        destination[offset + 4] = (byte)(_b >> 24);
        destination[offset + 5] = (byte)(_b >> 16);
        destination[offset + 6] = (byte)(_b >> 8);
        destination[offset + 7] = (byte)_b;
        destination[offset + 8] = (byte)(_c >> 24);
        destination[offset + 9] = (byte)(_c >> 16);
        destination[offset + 10] = (byte)(_c >> 8);
        destination[offset + 11] = (byte)_c;
    }

    /// <summary>
    ///     Returns a string representation of the value.
    /// </summary>
    /// <returns>A string representation of the value.</returns>
    public override string ToString()
    {
        var c = new char[24];
        c[0] = ((_a >> 28) & 0x0f).ToHexChar();
        c[1] = ((_a >> 24) & 0x0f).ToHexChar();
        c[2] = ((_a >> 20) & 0x0f).ToHexChar();
        c[3] = ((_a >> 16) & 0x0f).ToHexChar();
        c[4] = ((_a >> 12) & 0x0f).ToHexChar();
        c[5] = ((_a >> 8) & 0x0f).ToHexChar();
        c[6] = ((_a >> 4) & 0x0f).ToHexChar();
        c[7] = (_a & 0x0f).ToHexChar();
        c[8] = ((_b >> 28) & 0x0f).ToHexChar();
        c[9] = ((_b >> 24) & 0x0f).ToHexChar();
        c[10] = ((_b >> 20) & 0x0f).ToHexChar();
        c[11] = ((_b >> 16) & 0x0f).ToHexChar();
        c[12] = ((_b >> 12) & 0x0f).ToHexChar();
        c[13] = ((_b >> 8) & 0x0f).ToHexChar();
        c[14] = ((_b >> 4) & 0x0f).ToHexChar();
        c[15] = (_b & 0x0f).ToHexChar();
        c[16] = ((_c >> 28) & 0x0f).ToHexChar();
        c[17] = ((_c >> 24) & 0x0f).ToHexChar();
        c[18] = ((_c >> 20) & 0x0f).ToHexChar();
        c[19] = ((_c >> 16) & 0x0f).ToHexChar();
        c[20] = ((_c >> 12) & 0x0f).ToHexChar();
        c[21] = ((_c >> 8) & 0x0f).ToHexChar();
        c[22] = ((_c >> 4) & 0x0f).ToHexChar();
        c[23] = (_c & 0x0f).ToHexChar();
        return new string(c);
    }

    // explicit IConvertible implementation
    TypeCode IConvertible.GetTypeCode()
    {
        return TypeCode.Object;
    }

    bool IConvertible.ToBoolean(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    byte IConvertible.ToByte(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    char IConvertible.ToChar(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    DateTime IConvertible.ToDateTime(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    decimal IConvertible.ToDecimal(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    double IConvertible.ToDouble(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    short IConvertible.ToInt16(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    int IConvertible.ToInt32(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    long IConvertible.ToInt64(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    sbyte IConvertible.ToSByte(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    float IConvertible.ToSingle(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    string IConvertible.ToString(IFormatProvider? provider)
    {
        return ToString();
    }

    object IConvertible.ToType(Type conversionType, IFormatProvider? provider)
    {
        switch (Type.GetTypeCode(conversionType))
        {
            case TypeCode.String:
                return ((IConvertible)this).ToString(provider);
            case TypeCode.Object:
                if (conversionType == typeof(object) || conversionType == typeof(OctoObjectId))
                {
                    return this;
                }

                break;
        }

        throw new InvalidCastException();
    }

    ushort IConvertible.ToUInt16(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    uint IConvertible.ToUInt32(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }

    ulong IConvertible.ToUInt64(IFormatProvider? provider)
    {
        throw new InvalidCastException();
    }
}
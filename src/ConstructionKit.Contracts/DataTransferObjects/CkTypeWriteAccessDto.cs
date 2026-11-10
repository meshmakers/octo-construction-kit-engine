using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     CK v2 (AB#8574): restricts who may write (create, update, replace, delete, and change associations of) runtime
///     entities of a type. A write is allowed when the caller's principal kind is listed in <see cref="Principals" />
///     <b>or</b> the session carries one of the <see cref="Channels" />. System sessions (pipelines, blueprint seeds,
///     migrations) are never restricted. Derived types inherit the block; when a derived type declares its own block,
///     every block along the inheritance chain must allow the write. Requires <c>ckLanguage: 2</c>; a model that uses
///     it carries a raised <c>minEngineVersion</c> (<c>CkEngineVersion.WriteAccessMinEngineVersion</c>), so older
///     engines refuse it with message 126 instead of ignoring the restriction. The compiler validates the block
///     (message 143).
/// </summary>
public class CkTypeWriteAccessDto
{
    /// <summary>
    ///     The principal kinds that may write, see <see cref="CkWritePrincipals" /> (<c>Interactive</c>, <c>Client</c>).
    ///     <c>null</c> (omitted) means no principal kind is admitted by itself; then only a listed channel admits a
    ///     write.
    /// </summary>
    [YamlMember(Alias = "principals", DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Principals { get; set; }

    /// <summary>
    ///     Trusted in-process write channels in the form <c>&lt;namespace&gt;/&lt;name&gt;</c>, e.g.
    ///     <c>System.Ai/KnowledgeTools</c>. A channel is a marker that service code puts on the session it opens
    ///     (<c>RtSecurityContext.WriteChannel</c>); it never comes from a token, a header or a client id.
    /// </summary>
    [YamlMember(Alias = "channels", DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Channels { get; set; }
}

/// <summary>
///     The principal kinds of a <see cref="CkTypeWriteAccessDto" /> (AB#8574). The values are PascalCase, exactly as
///     authored; any other value is rejected by the compiler (message 143).
/// </summary>
public static class CkWritePrincipals
{
    /// <summary>
    ///     A human user signed in interactively (authorization code / device flow).
    /// </summary>
    public const string Interactive = "Interactive";

    /// <summary>
    ///     A service principal authenticated with client credentials.
    /// </summary>
    public const string Client = "Client";

    /// <summary>
    ///     True for a supported principal kind (ordinal).
    /// </summary>
    public static bool IsKnown(string? principal) => principal is Interactive or Client;

    /// <summary>
    ///     True when <paramref name="channel" /> has the form <c>&lt;namespace&gt;/&lt;name&gt;</c>: a namespace of
    ///     dot-separated identifiers (letters, digits, underscore; starting with a letter), a slash and a name of the
    ///     same characters.
    /// </summary>
    public static bool IsValidChannel(string? channel) =>
        channel != null && ChannelRegex.IsMatch(channel);

    private static readonly Regex ChannelRegex =
        new(@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)*/[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
}

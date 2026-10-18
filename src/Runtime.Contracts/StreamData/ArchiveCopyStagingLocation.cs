using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Where the files of a native archive copy lie, as the CrateDB nodes see them (AB#5967): the location URI of the
/// <c>COPY … TO DIRECTORY</c> / <c>COPY … FROM</c> statement plus the <c>WITH</c> options that carry the credential
/// (<c>protocol</c>, <c>sas_token</c>, <c>key</c>). Built by the host (octo-common-services) from the artifact store
/// settings; consumed by <see cref="IStreamDataRepository.CopyToStagingAsync"/> and
/// <see cref="IStreamDataRepository.CopyFromStagingAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// The endpoint in <see cref="RevealUri"/> is the one <b>CrateDB</b> uses. It can differ from the endpoint the bot
/// reaches the same store with (local development: the cluster name and port for CrateDB, the host port for the
/// bot), so this type is deliberately not the bot's artifact store configuration.
/// </para>
/// <para>
/// <b>The credential cannot leak through this type by accident.</b> The URI and the options are only reachable
/// through the methods <see cref="RevealUri"/> and <see cref="RevealOptions"/>; every property, <see cref="ToString"/>
/// and with it string interpolation, exception messages, structured logging and any reflection-based serializer
/// show the redacted form (<see cref="ArchiveCopyRedaction"/>). Callers that build a statement call the two
/// <c>Reveal…</c> methods and keep the result out of every log, exception and trace.
/// <see cref="Scrub"/> removes the credential from a text that may echo it, such as a driver error message.
/// </para>
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public sealed class ArchiveCopyStagingLocation
{
    private readonly string _uri;
    private readonly IReadOnlyDictionary<string, string> _options;

    /// <param name="crateUri">
    /// The location as CrateDB sees it, e.g. <c>s3://key:secret@minio.octo-infra:9000/bucket/prefix</c> or
    /// <c>az://account.blob.core.windows.net/container/prefix</c>. May contain the credential (S3 key pair).
    /// </param>
    /// <param name="withOptions">
    /// The <c>WITH</c> options of the statement that belong to the location, e.g. <c>protocol=http</c> or
    /// <c>sas_token=…</c>. Option names are case-insensitive.
    /// </param>
    /// <exception cref="ArgumentException">
    /// The URI has no scheme or an option has no name or no value. The message never contains the input.
    /// </exception>
    public ArchiveCopyStagingLocation(
        string crateUri, IEnumerable<KeyValuePair<string, string>>? withOptions = null)
    {
        if (string.IsNullOrWhiteSpace(crateUri))
        {
            throw new ArgumentException("The staging location URI must not be empty.", nameof(crateUri));
        }

        var schemeEnd = crateUri.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0)
        {
            throw new ArgumentException(
                "The staging location URI must start with a scheme, e.g. 's3://' or 'az://'.", nameof(crateUri));
        }

        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in withOptions ?? [])
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A staging location option needs a name.", nameof(withOptions));
            }

            if (value is null)
            {
                throw new ArgumentException($"The staging location option '{name}' needs a value.",
                    nameof(withOptions));
            }

            if (!options.TryAdd(name, value))
            {
                throw new ArgumentException($"The staging location option '{name}' is given twice.",
                    nameof(withOptions));
            }
        }

        _uri = crateUri;
        _options = options;
        Scheme = crateUri.Substring(0, schemeEnd).ToLowerInvariant();
    }

    /// <summary>The URI scheme in lower case, e.g. <c>s3</c>, <c>az</c> or <c>file</c>.</summary>
    public string Scheme { get; }

    /// <summary>The location without credential (<see cref="ArchiveCopyRedaction.RedactUri"/>).</summary>
    public string RedactedUri => ArchiveCopyRedaction.RedactUri(_uri);

    /// <summary>The <c>WITH</c> options with every value masked that is not known to be free of secrets.</summary>
    public IReadOnlyDictionary<string, string> RedactedOptions =>
        _options.ToDictionary(o => o.Key, o => ArchiveCopyRedaction.RedactOptionValue(o.Key, o.Value),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The location exactly as the statement needs it — <b>may contain the credential</b>. Use it to build the
    /// statement only; never log it, put it into an exception or hand it to tracing.
    /// </summary>
    public string RevealUri() => _uri;

    /// <summary>
    /// The <c>WITH</c> options exactly as the statement needs them — <b>may contain the credential</b>
    /// (<c>sas_token</c>, <c>key</c>). Same rules as <see cref="RevealUri"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> RevealOptions() => _options;

    /// <summary>
    /// The location of one sub-directory, e.g. <c>&lt;rtId&gt;/data/&lt;slice&gt;</c>
    /// (<see cref="ArchiveCopySlice.GetStagingPath"/>). The options stay.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The path is empty or has an empty, <c>.</c> or <c>..</c> segment or a backslash.
    /// </exception>
    public ArchiveCopyStagingLocation WithSubPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Contains('\\'))
        {
            throw new ArgumentException("The sub path must be a non-empty path with '/' separators.",
                nameof(relativePath));
        }

        var segments = relativePath.Trim('/').Split('/');
        if (segments.Any(s => s.Length == 0 || s is "." or ".."))
        {
            throw new ArgumentException("The sub path must not contain empty, '.' or '..' segments.",
                nameof(relativePath));
        }

        // The folder belongs to the path, so it goes in front of a query string or fragment (a signed URL keeps its signature).
        var queryOrFragment = _uri.IndexOfAny(['?', '#']);
        var pathEnd = queryOrFragment < 0 ? _uri.Length : queryOrFragment;
        var path = _uri.Substring(0, pathEnd).TrimEnd('/');
        return new ArchiveCopyStagingLocation(
            path + "/" + string.Join('/', segments) + _uri.Substring(pathEnd), _options);
    }

    /// <summary>
    /// Replaces every part of the credential that occurs in <paramref name="text"/> — user info of the URI, its
    /// key and secret, the values of all options not known to be free of secrets — with
    /// <see cref="ArchiveCopyRedaction.Mask"/>. For texts that may echo the statement, such as the message of a
    /// driver exception. The longest value is replaced first.
    /// </summary>
    public string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var secrets = ArchiveCopyRedaction.ExtractUriSecrets(_uri)
            .Concat(_options.Where(o => !ArchiveCopyRedaction.IsNonSecretOption(o.Key))
                .SelectMany(o => ArchiveCopyRedaction.Spellings(o.Value, minLength: 1)))
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(s => s.Length);

        var scrubbed = text;
        foreach (var secret in secrets)
        {
            scrubbed = scrubbed.Replace(secret, ArchiveCopyRedaction.Mask, StringComparison.Ordinal);
        }

        return scrubbed;
    }

    /// <summary>The redacted location and options, e.g. <c>s3://***@host:9000/bucket/p [key=***, protocol=http]</c>.</summary>
    public override string ToString()
    {
        var options = string.Join(", ",
            RedactedOptions.OrderBy(o => o.Key, StringComparer.OrdinalIgnoreCase).Select(o => $"{o.Key}={o.Value}"));
        return options.Length == 0 ? RedactedUri : $"{RedactedUri} [{options}]";
    }
}

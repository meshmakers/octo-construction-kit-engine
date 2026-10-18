using System;
using System.Collections.Generic;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// The one place that decides what of a staging location may be shown (AB#5967, concept
/// <c>concept-tenant-backup-native-archive-dump.md</c> §5). A <c>COPY</c> statement carries the storage
/// credential — the S3 key pair in the URI, the <c>sas_token</c> or <c>key</c> in the <c>WITH</c> clause — so
/// every text derived from it (log line, exception message, trace attribute) goes through this class.
/// </summary>
/// <remarks>
/// The rules are fail-safe: a <c>WITH</c> option is shown only when it is on the short list of options known to
/// carry no secret, every other value is masked. The user info of a URI (everything up to the last <c>@</c>
/// behind the scheme) is dropped as a whole, because an S3 secret key may contain <c>/</c> and <c>+</c> and cannot be
/// told apart from the host by a URI parser. A query string or fragment is masked. Over-redaction is accepted
/// (a prefix that itself contains <c>@</c> loses the part before it); under-redaction is not.
/// </remarks>
public static class ArchiveCopyRedaction
{
    /// <summary>Replacement for every value that must not be shown.</summary>
    public const string Mask = "***";

    private const string SchemeSeparator = "://";
    private const string Unparseable = "[unparseable location]";

    // COPY options that are known to carry no secret. Anything else (key, sas_token, account keys, options a
    // later CrateDB version adds) is masked.
    private static readonly HashSet<string> NonSecretOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "protocol",
        "compression",
        "format",
        "shared",
        "num_readers",
        "overwrite_duplicates",
        "wait_for_completion",
        "bulk_size",
        "fail_fast",
        "validation",
        "empty_string_as_null",
        "error_diagnostics"
    };

    /// <summary>True when the value of the <c>WITH</c> option <paramref name="key"/> may be shown.</summary>
    public static bool IsNonSecretOption(string key) => NonSecretOptions.Contains(key);

    /// <summary>The value of a <c>WITH</c> option as it may be shown: the value itself or <see cref="Mask"/>.</summary>
    public static string RedactOptionValue(string key, string value) => IsNonSecretOption(key) ? value : Mask;

    /// <summary>
    /// The location with the credential removed: scheme, host, bucket or container and prefix stay, the user info
    /// becomes <c>***@</c>, a query string or fragment becomes <c>?***</c>. Never throws and never echoes the
    /// input when it cannot be read.
    /// </summary>
    public static string RedactUri(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
        {
            return string.Empty;
        }

        var schemeEnd = uri.IndexOf(SchemeSeparator, StringComparison.Ordinal);
        if (schemeEnd <= 0)
        {
            return Unparseable;
        }

        var scheme = uri.Substring(0, schemeEnd);
        var rest = uri.Substring(schemeEnd + SchemeSeparator.Length);

        var at = rest.LastIndexOf('@');
        var hasUserInfo = at >= 0;
        var afterUserInfo = hasUserInfo ? rest.Substring(at + 1) : rest;

        var queryStart = afterUserInfo.IndexOfAny(['?', '#']);
        var hasQuery = queryStart >= 0;
        var location = hasQuery ? afterUserInfo.Substring(0, queryStart) : afterUserInfo;

        return scheme + SchemeSeparator + (hasUserInfo ? Mask + "@" : string.Empty) + location +
               (hasQuery ? "?" + Mask : string.Empty);
    }

    /// <summary>
    /// The values inside <paramref name="uri"/> that are secrets: the whole user info, and — for the usual
    /// <c>key:secret</c> form — each of its two parts, plus the whole query string or fragment and each of its
    /// parameter values (a signed URL carries its signature there, and <see cref="RedactUri"/> masks it as a whole).
    /// Each is also percent-decoded and percent-encoded, so a text that echoes only one of them or the other
    /// spelling is still caught. Parts shorter than three characters are not searched for on their own.
    /// </summary>
    internal static IEnumerable<string> ExtractUriSecrets(string uri)
    {
        var schemeEnd = uri.IndexOf(SchemeSeparator, StringComparison.Ordinal);
        if (schemeEnd <= 0)
        {
            yield break;
        }

        var rest = uri.Substring(schemeEnd + SchemeSeparator.Length);

        var queryStart = rest.IndexOfAny(['?', '#']);
        if (queryStart >= 0)
        {
            var query = rest.Substring(queryStart + 1);
            foreach (var spelling in Spellings(query, minLength: 1))
            {
                yield return spelling;
            }

            foreach (var parameter in query.Split(['&', ';', '#', '?'], StringSplitOptions.RemoveEmptyEntries))
            {
                var value = parameter.Substring(parameter.IndexOf('=') + 1);
                foreach (var spelling in Spellings(value, minLength: 3))
                {
                    yield return spelling;
                }
            }

            rest = rest.Substring(0, queryStart);
        }

        var at = rest.LastIndexOf('@');
        if (at <= 0)
        {
            yield break;
        }

        var userInfo = rest.Substring(0, at);
        foreach (var spelling in Spellings(userInfo, minLength: 1))
        {
            yield return spelling;
        }

        var colon = userInfo.IndexOf(':');
        if (colon < 0)
        {
            yield break;
        }

        foreach (var part in new[] { userInfo.Substring(0, colon), userInfo.Substring(colon + 1) })
        {
            if (part.Length < 3)
            {
                continue;
            }

            foreach (var spelling in Spellings(part, minLength: 3))
            {
                yield return spelling;
            }
        }
    }

    /// <summary>The value as written, percent-decoded and percent-encoded (each only once and when long enough).</summary>
    internal static IEnumerable<string> Spellings(string value, int minLength)
    {
        if (value.Length >= minLength)
        {
            yield return value;
        }

        var decoded = TryUnescape(value);
        if (decoded != value && decoded.Length >= minLength)
        {
            yield return decoded;
        }

        var encoded = Uri.EscapeDataString(value);
        if (encoded != value && encoded.Length >= minLength)
        {
            yield return encoded;
        }
    }

    private static string TryUnescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }
}

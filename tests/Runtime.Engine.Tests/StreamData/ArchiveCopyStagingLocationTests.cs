using System;
using System.Collections.Generic;
using System.Text.Json;
using Meshmakers.Octo.Runtime.Contracts.StreamData;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Runtime.Engine.Tests.StreamData;

/// <summary>
///     AB#5967 (slice AB#6967): a staging location carries the storage credential in its URI (S3 key pair) and in
///     its WITH options (sas_token, key). No way to turn it into text may show a canary of the credential.
/// </summary>
public class ArchiveCopyStagingLocationTests
{
    // The secret contains '/' and '+' on purpose: a real S3 secret key does, and a URI parser would cut at the '/'.
    private const string CanaryKeyId = "CANARYKEYID1234";
    private const string CanarySecret = "CANARY/SECRET+value==";
    private const string CanarySas = "sv=CANARYSAS&sig=CANARYSIG";
    private const string CanaryAccountKey = "CANARYACCOUNTKEY==";

    private static readonly string[] AllCanaries =
    [
        CanaryKeyId, CanarySecret, "CANARY/SECRET", "SECRET+value", "CANARY%2FSECRET", Uri.EscapeDataString(CanarySecret),
        CanarySas, "CANARYSIG", Uri.EscapeDataString(CanarySas), CanaryAccountKey
    ];

    private static ArchiveCopyStagingLocation S3() =>
        new($"s3://{CanaryKeyId}:{CanarySecret}@minio.octo-infra:9000/octo-artifacts/copy-staging/tenant1/run1",
            new Dictionary<string, string>
            {
                ["protocol"] = "http", ["sas_token"] = CanarySas, ["key"] = CanaryAccountKey
            });

    private static ArchiveCopyStagingLocation Az() =>
        new("az://acct.blob.core.windows.net/octo-artifacts/copy-staging/tenant1/run1",
            new Dictionary<string, string> { ["sas_token"] = CanarySas });

    private static void AssertNoCanary(string text, string surface)
    {
        foreach (var canary in AllCanaries)
        {
            Assert.False(text.Contains(canary, StringComparison.Ordinal), $"{surface} shows '{canary}': {text}");
        }
    }

    [Fact]
    public void ToString_S3_NamesSchemeHostBucketAndPrefixButNoCredential()
    {
        var text = S3().ToString();

        AssertNoCanary(text, "ToString");
        Assert.Contains("s3://", text);
        Assert.Contains("minio.octo-infra:9000", text);
        Assert.Contains("octo-artifacts/copy-staging/tenant1/run1", text);
        Assert.Contains("protocol=http", text);
        Assert.Contains("sas_token=***", text);
        Assert.Contains("key=***", text);
    }

    [Fact]
    public void ToString_Az_NamesAccountContainerAndPrefixButNoToken()
    {
        var text = Az().ToString();

        AssertNoCanary(text, "ToString");
        Assert.Contains("az://acct.blob.core.windows.net/octo-artifacts/copy-staging/tenant1/run1", text);
        Assert.Contains("sas_token=***", text);
    }

    [Fact]
    public void EveryWayToTurnTheLocationIntoText_ShowsNoCredential()
    {
        var location = S3();

        AssertNoCanary($"{location}", "string interpolation");
        AssertNoCanary(string.Format("{0}", location), "string.Format");
        AssertNoCanary(new InvalidOperationException($"copy failed for {location}").ToString(), "exception text");
        AssertNoCanary(System.Text.Json.JsonSerializer.Serialize(location), "System.Text.Json");
        AssertNoCanary(JsonConvert.SerializeObject(location), "Newtonsoft.Json");
        AssertNoCanary(JsonConvert.SerializeObject(new { location }), "Newtonsoft.Json (nested)");
        AssertNoCanary(System.Text.Json.JsonSerializer.Serialize(new { location },
            new JsonSerializerOptions { WriteIndented = true }), "System.Text.Json (nested)");
        AssertNoCanary(string.Join(",", location.RedactedOptions), "RedactedOptions");
        AssertNoCanary(location.RedactedUri, "RedactedUri");
    }

    [Fact]
    public void Reveal_ReturnsTheUriAndOptionsTheStatementNeeds()
    {
        var location = S3();

        Assert.Equal(
            $"s3://{CanaryKeyId}:{CanarySecret}@minio.octo-infra:9000/octo-artifacts/copy-staging/tenant1/run1",
            location.RevealUri());
        Assert.Equal(CanarySas, location.RevealOptions()["sas_token"]);
        Assert.Equal("http", location.RevealOptions()["PROTOCOL"]); // names are case-insensitive
        Assert.Equal("s3", location.Scheme);
    }

    [Fact]
    public void UnknownOption_IsMaskedBecauseTheListOfHarmlessOptionsIsClosed()
    {
        var location = new ArchiveCopyStagingLocation("s3://host/bucket",
            new Dictionary<string, string> { ["some_future_token"] = "CANARYFUTURE", ["compression"] = "gzip" });

        var text = location.ToString();

        Assert.DoesNotContain("CANARYFUTURE", text);
        Assert.Contains("some_future_token=***", text);
        Assert.Contains("compression=gzip", text);
        Assert.DoesNotContain("CANARYFUTURE", location.Scrub("echo CANARYFUTURE"));
    }

    [Theory]
    [InlineData("s3://host:9000/bucket/p?X-Amz-Signature=CANARYSIG", "s3://host:9000/bucket/p?***")]
    [InlineData("az://acct.blob.core.windows.net/c/p#CANARYSIG", "az://acct.blob.core.windows.net/c/p?***")]
    [InlineData("file:///tmp/copy/x", "file:///tmp/copy/x")]
    [InlineData("s3://K:S@host/b/p", "s3://***@host/b/p")]
    public void RedactUri_KeepsSchemeHostAndPathAndMasksUserInfoAndQuery(string uri, string expected)
    {
        Assert.Equal(expected, ArchiveCopyRedaction.RedactUri(uri));
    }

    [Fact]
    public void RedactUri_WithoutScheme_NeverEchoesTheInput()
    {
        var text = ArchiveCopyRedaction.RedactUri("CANARYKEYID1234:CANARYSECRET@host/bucket");

        Assert.DoesNotContain("CANARY", text);
    }

    [Fact]
    public void Scrub_RemovesTheWholeCredentialAndEachPartOfIt_EvenPercentEncoded()
    {
        var location = S3();
        var echoes = new[]
        {
            $"statement COPY t TO DIRECTORY 's3://{CanaryKeyId}:{CanarySecret}@minio.octo-infra:9000/b' failed",
            $"access key {CanaryKeyId} was rejected",
            $"signature for {CanarySecret} does not match",
            $"bad token {CanarySas} and key {CanaryAccountKey}",
            $"url s3://{CanaryKeyId}:{Uri.EscapeDataString(CanarySecret)}@host/b"
        };

        foreach (var echo in echoes)
        {
            AssertNoCanary(location.Scrub(echo), "Scrub");
        }

        // the harmless part of the message survives
        Assert.Contains("was rejected", location.Scrub(echoes[1]));
        Assert.Contains("minio.octo-infra:9000/b", location.Scrub(echoes[0]));
    }

    [Fact]
    public void Scrub_OnNullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, S3().Scrub(null));
        Assert.Equal(string.Empty, S3().Scrub(string.Empty));
    }

    [Fact]
    public void WithSubPath_AppendsTheFolderAndKeepsTheOptions()
    {
        var sub = S3().WithSubPath("0123456789abcdef01234567/data/20260101T000000000_20260102T000000000");

        Assert.EndsWith("/run1/0123456789abcdef01234567/data/20260101T000000000_20260102T000000000",
            sub.RevealUri());
        Assert.Equal(CanarySas, sub.RevealOptions()["sas_token"]);
        AssertNoCanary(sub.ToString(), "sub location");
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/../b")]
    [InlineData("a//b")]
    [InlineData("./a")]
    [InlineData("a\\b")]
    public void WithSubPath_RefusesPathsThatCouldLeaveTheStagingArea(string path)
    {
        Assert.Throws<ArgumentException>(() => S3().WithSubPath(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("CANARYNOSCHEME")]
    [InlineData("://CANARYNOHOST")]
    public void Constructor_RefusesAnUnusableUri_WithoutEchoingIt(string uri)
    {
        var ex = Assert.Throws<ArgumentException>(() => new ArchiveCopyStagingLocation(uri));

        Assert.DoesNotContain("CANARY", ex.Message);
    }

    [Fact]
    public void Constructor_RefusesDuplicateAndNamelessOptions_WithoutEchoingTheValue()
    {
        var duplicate = Assert.Throws<ArgumentException>(() => new ArchiveCopyStagingLocation("s3://h/b",
            [new("sas_token", "CANARYONE"), new("SAS_TOKEN", "CANARYTWO")]));
        var nameless = Assert.Throws<ArgumentException>(() => new ArchiveCopyStagingLocation("s3://h/b",
            [new(" ", "CANARYONE")]));

        Assert.DoesNotContain("CANARY", duplicate.Message);
        Assert.Contains("sas_token", duplicate.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CANARY", nameless.Message);
    }
}

using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.Runtime.Contracts.TransportContainer.DTOs;
using Meshmakers.Octo.Runtime.Engine.Blueprints;

using Xunit;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Blueprints;

/// <summary>
/// Unit tests for <see cref="BlueprintVariableInterpolator"/>. The interpolator is a pure
/// in-memory walker, so tests construct <see cref="RtModelRootTcDto"/> by hand instead of
/// going through the YAML serializer.
/// </summary>
public class BlueprintVariableInterpolatorTests
{
    [Fact]
    public void Interpolate_ReplacesPlaceholderInStringAttribute()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["octo.version"] = "1.2.3",
        };
        var root = MakeRoot(
            attributeValue: "chart-${octo.version}",
            wellKnownName: null);

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, variables, "test.yaml", op);

        Assert.Equal("chart-1.2.3", root.Entities[0].Attributes[0].Value);
        Assert.False(op.HasErrors);
        Assert.Empty(op.Messages);
    }

    [Fact]
    public void Interpolate_ReplacesPlaceholderInRtWellKnownName()
    {
        // rtWellKnownName participates in interpolation too — needed so blueprints can
        // produce environment-scoped well-known names (rare, but consistent with attribute
        // values).
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["octo.environment"] = "staging",
        };
        var root = MakeRoot(
            attributeValue: "static",
            wellKnownName: "cockpit-${octo.environment}");

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, variables, "test.yaml", op);

        Assert.Equal("cockpit-staging", root.Entities[0].RtWellKnownName);
    }

    [Fact]
    public void Interpolate_LeavesNonStringAttributeValuesUntouched()
    {
        // The substitution path only touches `value is string` — numbers, bools and
        // embedded records must round-trip unchanged. Regression guard for accidental
        // ToString-then-replace on non-string scalars.
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["octo.version"] = "1.2.3",
        };
        var root = new RtModelRootTcDto
        {
            Entities =
            {
                new RtEntityTcDto
                {
                    RtId = new OctoObjectId("abcdef1234567890abcdef12"),
                    CkTypeId = new RtCkId<CkTypeId>("System.Test/Sample-1"),
                    Attributes =
                    {
                        new RtAttributeTcDto
                        {
                            Id = new RtCkId<CkAttributeId>("System/Number"),
                            Value = 42 // non-string
                        },
                    }
                }
            }
        };

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, variables, "test.yaml", op);

        Assert.Equal(42, root.Entities[0].Attributes[0].Value);
    }

    [Fact]
    public void Interpolate_UnknownVariable_LogsWarningAndLeavesPlaceholder()
    {
        // Better to ship a manifest with an obvious placeholder than to silently write
        // empty strings into MongoDB. The warning fires once per occurrence so authors get
        // a clear pointer to every typo.
        var variables = new Dictionary<string, string>(StringComparer.Ordinal); // empty
        var root = MakeRoot(
            attributeValue: "chart-${octo.version}",
            wellKnownName: null);

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, variables, "test.yaml", op);

        Assert.Equal("chart-${octo.version}", root.Entities[0].Attributes[0].Value);
        Assert.False(op.HasErrors); // warnings only, no errors
        Assert.Contains(op.Messages,
            m => m.MessageLevel == MessageLevel.Warning
                 && m.MessageText.Contains("octo.version"));
    }

    [Fact]
    public void Interpolate_StringWithoutPlaceholder_IsFastPathNoOp()
    {
        // The interpolator short-circuits via IndexOf("${"). Tests sanity-check that a
        // value containing $ but no ${ syntax is not modified — guards against regex
        // edge-cases where a stray dollar sign would have triggered a replace.
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["octo.version"] = "1.2.3",
        };
        var root = MakeRoot(
            attributeValue: "price: $42.50",
            wellKnownName: null);

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, variables, "test.yaml", op);

        Assert.Equal("price: $42.50", root.Entities[0].Attributes[0].Value);
    }

    [Fact]
    public void Interpolate_ReplacesPlaceholdersInsideListValuedAttribute()
    {
        // Attributes like RedirectUris, AllowedCorsOrigins and AllowedScopes carry a list of
        // strings, each of which may contain its own placeholder. Without per-item
        // interpolation the engine would silently write the literal "${...}" placeholder into
        // MongoDB for every list entry, which is exactly the regression that surfaced on
        // System.Identity.Bootstrap-1.0.0's RefineryStudioClient (the FrontChannelLogoutUri
        // string was resolved but the list-valued RedirectUris / AllowedCorsOrigins were not,
        // so OIDC redirect-URI validation rejected every login).
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["octo.identity.refineryStudioUrl"] = "https://studio.test.octo-mesh.com",
        };
        var root = new RtModelRootTcDto
        {
            Entities =
            {
                new RtEntityTcDto
                {
                    RtId = new OctoObjectId("abcdef1234567890abcdef12"),
                    CkTypeId = new RtCkId<CkTypeId>("System.Test/Sample-1"),
                    Attributes =
                    {
                        new RtAttributeTcDto
                        {
                            Id = new RtCkId<CkAttributeId>("System.Identity/RedirectUris"),
                            Value = new List<object>
                            {
                                "${octo.identity.refineryStudioUrl}/",
                                "${octo.identity.refineryStudioUrl}/signin-oidc",
                            },
                        },
                    },
                },
            },
        };

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, variables, "test.yaml", op);

        var resolved = Assert.IsType<List<object>>(root.Entities[0].Attributes[0].Value);
        Assert.Equal("https://studio.test.octo-mesh.com/", resolved[0]);
        Assert.Equal("https://studio.test.octo-mesh.com/signin-oidc", resolved[1]);
        Assert.False(op.HasErrors);
        Assert.Empty(op.Messages);
    }

    [Fact]
    public void Interpolate_ListWithMixedStringsAndNonStrings_OnlyInterpolatesStrings()
    {
        // Defence in depth: a list-valued attribute can in principle hold non-string items
        // (e.g. a CK enum stored as its numeric key, or an embedded record). The interpolator
        // must walk every item but only rewrite the strings; numeric entries must round-trip.
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["octo.environment"] = "staging",
        };
        var root = new RtModelRootTcDto
        {
            Entities =
            {
                new RtEntityTcDto
                {
                    RtId = new OctoObjectId("abcdef1234567890abcdef12"),
                    CkTypeId = new RtCkId<CkTypeId>("System.Test/Sample-1"),
                    Attributes =
                    {
                        new RtAttributeTcDto
                        {
                            Id = new RtCkId<CkAttributeId>("System.Test/Mixed"),
                            Value = new List<object>
                            {
                                "env-${octo.environment}",
                                42,
                                true,
                            },
                        },
                    },
                },
            },
        };

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, variables, "test.yaml", op);

        var resolved = Assert.IsType<List<object>>(root.Entities[0].Attributes[0].Value);
        Assert.Equal("env-staging", resolved[0]);
        Assert.Equal(42, resolved[1]);
        Assert.Equal(true, resolved[2]);
    }

    [Fact]
    public void Interpolate_ListWithUnknownVariable_WarnsAndLeavesItemUnchanged()
    {
        // Same warn-and-leave-unchanged contract as for scalar string values — a typo in a
        // list item must surface in the operation result so authors can spot it.
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        var root = new RtModelRootTcDto
        {
            Entities =
            {
                new RtEntityTcDto
                {
                    RtId = new OctoObjectId("abcdef1234567890abcdef12"),
                    CkTypeId = new RtCkId<CkTypeId>("System.Test/Sample-1"),
                    Attributes =
                    {
                        new RtAttributeTcDto
                        {
                            Id = new RtCkId<CkAttributeId>("System.Test/Uris"),
                            Value = new List<object> { "${octo.nope}/foo" },
                        },
                    },
                },
            },
        };

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, variables, "test.yaml", op);

        var resolved = Assert.IsType<List<object>>(root.Entities[0].Attributes[0].Value);
        Assert.Equal("${octo.nope}/foo", resolved[0]);
        Assert.Contains(op.Messages,
            m => m.MessageLevel == MessageLevel.Warning && m.MessageText.Contains("octo.nope"));
    }

    [Fact]
    public void Interpolate_DescendsIntoRecordArrayItems()
    {
        // Regression guard for AB#4209 Step 1: Identity's RedirectUris /
        // PostLogoutRedirectUris / AllowedCorsOrigins flipped from StringArray to
        // RecordArray<ClientUriEntry>. Each list entry is now an RtRecordTcDto with a Uri
        // string + Source string inside, and the seed YAML still carries ${...} placeholders
        // on the Uri value. Without record-descent the placeholder lands in MongoDB
        // verbatim and Duende's ValidatingClientStore rejects the client with
        // "AllowedCorsOrigins contains invalid origin".
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["octo.mcp.publicUrl"] = "https://localhost:5017",
        };
        var record = new RtRecordTcDto
        {
            CkRecordId = new RtCkId<CkRecordId>("System.Identity/ClientUriEntry"),
            Attributes =
            {
                new RtAttributeTcDto
                {
                    Id = new RtCkId<CkAttributeId>("System.Identity/Uri"),
                    Value = "${octo.mcp.publicUrl}/swagger/oauth2-redirect.html"
                },
                new RtAttributeTcDto
                {
                    Id = new RtCkId<CkAttributeId>("System.Identity/Source"),
                    Value = "base"
                }
            }
        };
        var root = new RtModelRootTcDto
        {
            Entities =
            {
                new RtEntityTcDto
                {
                    RtId = new OctoObjectId("abcdef1234567890abcdef12"),
                    CkTypeId = new RtCkId<CkTypeId>("System.Identity/Client"),
                    Attributes =
                    {
                        new RtAttributeTcDto
                        {
                            Id = new RtCkId<CkAttributeId>("System.Identity/RedirectUris"),
                            Value = new List<object> { record }
                        }
                    }
                }
            }
        };

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, variables, "test.yaml", op);

        var resolvedList = Assert.IsType<List<object>>(root.Entities[0].Attributes[0].Value);
        var resolvedRecord = Assert.IsType<RtRecordTcDto>(resolvedList[0]);
        Assert.Equal("https://localhost:5017/swagger/oauth2-redirect.html", resolvedRecord.Attributes[0].Value);
        Assert.Equal("base", resolvedRecord.Attributes[1].Value);
        Assert.False(op.HasErrors);
        Assert.Empty(op.Messages);
    }

    [Fact]
    public void Interpolate_DescendsIntoSingleRecordValue()
    {
        // Sibling case to RecordArray descent: a single-Record-typed attribute
        // (e.g. Basic/TimeRange) carrying a placeholder must also be interpolated.
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["octo.scheme"] = "https",
        };
        var record = new RtRecordTcDto
        {
            CkRecordId = new RtCkId<CkRecordId>("System.Test/Endpoint"),
            Attributes =
            {
                new RtAttributeTcDto
                {
                    Id = new RtCkId<CkAttributeId>("System.Test/Url"),
                    Value = "${octo.scheme}://example.com"
                }
            }
        };
        var root = new RtModelRootTcDto
        {
            Entities =
            {
                new RtEntityTcDto
                {
                    RtId = new OctoObjectId("abcdef1234567890abcdef12"),
                    CkTypeId = new RtCkId<CkTypeId>("System.Test/Sample"),
                    Attributes =
                    {
                        new RtAttributeTcDto
                        {
                            Id = new RtCkId<CkAttributeId>("System.Test/Endpoint"),
                            Value = record
                        }
                    }
                }
            }
        };

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, variables, "test.yaml", op);

        var resolved = Assert.IsType<RtRecordTcDto>(root.Entities[0].Attributes[0].Value);
        Assert.Equal("https://example.com", resolved.Attributes[0].Value);
    }

    private static RtModelRootTcDto MakeRoot(string attributeValue, string? wellKnownName)
    {
        return new RtModelRootTcDto
        {
            Entities =
            {
                new RtEntityTcDto
                {
                    RtId = new OctoObjectId("abcdef1234567890abcdef12"),
                    CkTypeId = new RtCkId<CkTypeId>("System.Test/Sample-1"),
                    RtWellKnownName = wellKnownName,
                    Attributes =
                    {
                        new RtAttributeTcDto
                        {
                            Id = new RtCkId<CkAttributeId>("System/Name"),
                            Value = attributeValue
                        }
                    }
                }
            }
        };
    }
}

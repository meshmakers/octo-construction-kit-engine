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

    // ---- AB#8995: escape $${name} -> literal ${name} ----------------------------------------

    private static Dictionary<string, string> TenantVariables() => new(StringComparer.Ordinal)
    {
        ["tenantId"] = "sbeg",
    };

    private static void AssertNoUnknownVariableWarning(OperationResult op) =>
        Assert.DoesNotContain(op.Messages, m => m.MessageNumber == 27);

    [Fact]
    public void Interpolate_EscapedPlaceholder_IsWrittenAsLiteral_WithoutWarning()
    {
        var root = MakeRoot(attributeValue: "Failed: $${count} processes", wellKnownName: null);

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, TenantVariables(), "test.yaml", op);

        Assert.Equal("Failed: ${count} processes", root.Entities[0].Attributes[0].Value);
        Assert.Empty(op.Messages);
    }

    [Fact]
    public void Interpolate_EscapedPlaceholder_IsUnescaped_WhenTheBlueprintHasNoVariables()
    {
        var root = MakeRoot(attributeValue: "$${count}", wellKnownName: null);

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(
            root, new Dictionary<string, string>(StringComparer.Ordinal), "test.yaml", op);

        Assert.Equal("${count}", root.Entities[0].Attributes[0].Value);
        Assert.Empty(op.Messages);
    }

    [Fact]
    public void Interpolate_EscapedKnownVariable_IsNotSubstituted()
    {
        // The escape wins over a variable of the same name: the author asked for the literal.
        var root = MakeRoot(attributeValue: "$${tenantId}", wellKnownName: null);

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, TenantVariables(), "test.yaml", op);

        Assert.Equal("${tenantId}", root.Entities[0].Attributes[0].Value);
        Assert.Empty(op.Messages);
    }

    [Fact]
    public void Interpolate_UnescapedKnownVariable_IsStillSubstituted()
    {
        var root = MakeRoot(attributeValue: "tenant ${tenantId}", wellKnownName: null);

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, TenantVariables(), "test.yaml", op);

        Assert.Equal("tenant sbeg", root.Entities[0].Attributes[0].Value);
        Assert.Empty(op.Messages);
    }

    [Fact]
    public void Interpolate_UnescapedUnknownVariable_KeepsTodaysWarning()
    {
        var root = MakeRoot(attributeValue: "${count}", wellKnownName: null);

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, TenantVariables(), "test.yaml", op);

        Assert.Equal("${count}", root.Entities[0].Attributes[0].Value);
        var warning = Assert.Single(op.Messages);
        Assert.Equal(MessageLevel.Warning, warning.MessageLevel);
        Assert.Equal(27, warning.MessageNumber);
        Assert.Contains("${count}", warning.MessageText);
    }

    [Fact]
    public void Interpolate_MixedEscapedAndKnownVariable_InOneString()
    {
        var root = MakeRoot(attributeValue: "$${count} of ${tenantId}", wellKnownName: null);

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, TenantVariables(), "test.yaml", op);

        Assert.Equal("${count} of sbeg", root.Entities[0].Attributes[0].Value);
        Assert.Empty(op.Messages);
    }

    [Fact]
    public void Interpolate_MixedEscapedAndUnknownVariable_WarnsOnlyForTheUnknownOne()
    {
        var root = MakeRoot(attributeValue: "$${count} / ${typo} / $${name}", wellKnownName: null);

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, TenantVariables(), "test.yaml", op);

        Assert.Equal("${count} / ${typo} / ${name}", root.Entities[0].Attributes[0].Value);
        var warning = Assert.Single(op.Messages);
        Assert.Equal(27, warning.MessageNumber);
        Assert.Contains("${typo}", warning.MessageText);
    }

    [Fact]
    public void Interpolate_EscapedPlaceholder_InRtWellKnownName()
    {
        var root = MakeRoot(attributeValue: "static", wellKnownName: "template-$${name}");

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, TenantVariables(), "test.yaml", op);

        Assert.Equal("template-${name}", root.Entities[0].RtWellKnownName);
        Assert.Empty(op.Messages);
    }

    [Theory]
    [InlineData("${a->b}", "${a->b}")]       // unescaped arrow path: no variable name, verbatim, no warning (as before)
    [InlineData("$${a->b}", "${a->b}")]      // escaped arrow path: unescaped, no warning
    [InlineData("$${ count }", "${ count }")] // the text between the braces is kept as written
    [InlineData("$$${count}", "$${count}")]  // only the $$ directly before { is the escape
    [InlineData("cost: $$5", "cost: $$5")]   // $$ not followed by { is left alone
    public void Interpolate_EscapeEdgeCases_AreLiteral_WithoutWarning(string seed, string expected)
    {
        var root = MakeRoot(attributeValue: seed, wellKnownName: null);

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, TenantVariables(), "test.yaml", op);

        Assert.Equal(expected, root.Entities[0].Attributes[0].Value);
        AssertNoUnknownVariableWarning(op);
    }

    [Fact]
    public void Interpolate_EscapedPlaceholders_InStringListsAndNestedRecords()
    {
        // String list items, a RecordArray entry with a nested single record, and a
        // single-Record attribute - every walked position honours the escape.
        var inner = new RtRecordTcDto
        {
            CkRecordId = new RtCkId<CkRecordId>("System.Test/Inner"),
            Attributes =
            {
                new RtAttributeTcDto
                {
                    Id = new RtCkId<CkAttributeId>("System.Test/Text"),
                    Value = "inner $${meteringPointNumber} for ${tenantId}"
                }
            }
        };
        var arrayEntry = new RtRecordTcDto
        {
            CkRecordId = new RtCkId<CkRecordId>("System.Test/Line"),
            Attributes =
            {
                new RtAttributeTcDto
                {
                    Id = new RtCkId<CkAttributeId>("System.Test/Text"),
                    Value = "line $${startTime}"
                },
                new RtAttributeTcDto
                {
                    Id = new RtCkId<CkAttributeId>("System.Test/Inner"),
                    Value = inner
                }
            }
        };
        var single = new RtRecordTcDto
        {
            CkRecordId = new RtCkId<CkRecordId>("System.Test/Single"),
            Attributes =
            {
                new RtAttributeTcDto
                {
                    Id = new RtCkId<CkAttributeId>("System.Test/Text"),
                    Value = "single $${responseCode}"
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
                    CkTypeId = new RtCkId<CkTypeId>("System.Test/Sample-1"),
                    Attributes =
                    {
                        new RtAttributeTcDto
                        {
                            Id = new RtCkId<CkAttributeId>("System.Test/Tags"),
                            Value = new List<object> { "$${info}", 7, "${tenantId}" }
                        },
                        new RtAttributeTcDto
                        {
                            Id = new RtCkId<CkAttributeId>("System.Test/Lines"),
                            Value = new List<object> { arrayEntry }
                        },
                        new RtAttributeTcDto
                        {
                            Id = new RtCkId<CkAttributeId>("System.Test/Single"),
                            Value = single
                        }
                    }
                }
            }
        };

        var op = new OperationResult();
        BlueprintVariableInterpolator.Interpolate(root, TenantVariables(), "test.yaml", op);

        var tags = Assert.IsType<List<object>>(root.Entities[0].Attributes[0].Value);
        Assert.Equal(new object[] { "${info}", 7, "sbeg" }, tags);
        var lines = Assert.IsType<List<object>>(root.Entities[0].Attributes[1].Value);
        var line = Assert.IsType<RtRecordTcDto>(lines[0]);
        Assert.Equal("line ${startTime}", line.Attributes[0].Value);
        var nested = Assert.IsType<RtRecordTcDto>(line.Attributes[1].Value);
        Assert.Equal("inner ${meteringPointNumber} for sbeg", nested.Attributes[0].Value);
        var singleResolved = Assert.IsType<RtRecordTcDto>(root.Entities[0].Attributes[2].Value);
        Assert.Equal("single ${responseCode}", singleResolved.Attributes[0].Value);
        Assert.Empty(op.Messages);
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

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

using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.SourceGeneration;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SourceGeneration;

/// <summary>
///     AB#5532 (WP1 follow-up): the engine source generator emits <c>RtSecretValue?</c> for a Secret
///     attribute - never <c>string</c> - for optional and required attributes alike.
/// </summary>
public class AttributeCodeGeneratorSecretTests
{
    private static (CkTypeAttributeDto Assignment, CkAttributeGraph Definition) SecretAttribute(bool isOptional)
    {
        var attributeId = new CkId<CkAttributeId>("Test-1.0.0/Password");
        var definition = new CkAttributeGraph(attributeId, new CkAttributeDto
        {
            AttributeId = "Password",
            ValueType = AttributeValueTypesDto.Secret,
            Ownership = AttributeOwnershipDto.Secret
        });
        var assignment = new CkTypeAttributeDto
        {
            CkAttributeId = attributeId, AttributeName = "Password", IsOptional = isOptional
        };
        return (assignment, definition);
    }

    [Fact]
    public void GenerateNullableProperty_Secret_EmitsRtSecretValue()
    {
        var (assignment, definition) = SecretAttribute(true);
        var sb = new StringBuilder();

        AttributeCodeGenerator.GenerateNullableProperty(assignment, sb, definition);

        var code = sb.ToString();
        Assert.Contains("public RtSecretValue? Password", code);
        Assert.Contains("GetAttributeSecretValueOrDefault(nameof(Password))", code);
        Assert.Contains("AttributeValueTypesDto.Secret", code);
        Assert.DoesNotContain("string", code);
        Assert.DoesNotContain("Unsupported by Generator", code);
    }

    [Fact]
    public void GenerateNonNullableProperty_Secret_EmitsNullableRtSecretValue()
    {
        var (assignment, definition) = SecretAttribute(false);
        var sb = new StringBuilder();

        AttributeCodeGenerator.GenerateNonNullableProperty(assignment, sb, definition);

        var code = sb.ToString();
        // A required secret is still nullable on read: reads only tell whether it is set.
        Assert.Contains("public RtSecretValue? Password", code);
        Assert.Contains("GetAttributeSecretValueOrDefault(nameof(Password))", code);
        Assert.DoesNotContain("GetAttributeStringValue", code);
        Assert.DoesNotContain("Unsupported by Generator", code);
    }
}

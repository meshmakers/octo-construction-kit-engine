

using Meshmakers.ConstructionKit.Samples.SourceGenerator.ConstructionKit.Generated.Sample1.v1;

namespace Meshmakers.ConstructionKit.Samples.SourceGenerator;

public enum AttributeValueTypes
{
    Int,
    String,
    Boolean
}


public class RtSystemEntity
{
    public RtSystemEntity()
    {
    }

    public TValue? GetAttributeValueOrDefault<TValue>(string attributeName, TValue? defaultValue = default)
        where TValue : struct
    {
        return null;
    }

    public string? GetAttributeStringValueOrDefault(string attributeName)
    {
        return null;
    }

    public object? GetAttributeValueOrDefault(string attributeName, object? defaultValue = default)
    {
        return null;
    }

    public void SetAttributeValue(string attributeName, AttributeValueTypes attributeValueType, object? value)
    {
    }
}

public class Test
{
    public Test()
    {
        RtSample1SampleType1 sampleType1 = new()
        {
            MyAttribute = "test"
        };
    }
}
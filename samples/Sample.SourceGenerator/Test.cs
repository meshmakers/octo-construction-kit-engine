namespace Meshmakers.ConstructionKit.Samples.SourceGenerator;

public enum AttributeValueTypes
{
    Int,
    String,
    Boolean,
    DateTime
}

public class RtRecord : RtEntity
{
    
}

public class RtEntity
{
    public RtEntity()
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
    
    public string GetAttributeStringValue(string attributeName)
    {
        throw new InvalidOperationException();
    }

    public object? GetAttributeValueOrDefault(string attributeName, object? defaultValue = default)
    {
        return null;
    }
    
    public TValue GetAttributeValue<TValue>(string attributeName, TValue defaultValue = default)
        where TValue : struct
    {
        throw new InvalidOperationException();
    }

    public void SetAttributeValue(string attributeName, AttributeValueTypes attributeValueType, object? value)
    {
    }
    
    public void SetAttributeValueNonNullable(string attributeName, AttributeValueTypes attributeValueType, object value)
    {
    }
}

public class Test
{
    public Test()
    {
        RtSampleType1 sampleType1 = new()
        {
            MyAttribute = "test"
        };
    }
}
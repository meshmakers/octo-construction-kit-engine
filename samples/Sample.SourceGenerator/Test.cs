using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

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
    public CkId<CkRecordId> CkRecordId { get; set; }
    
    public RtRecord()
    {
    }
    
    public RtRecord(CkId<CkRecordId> ckRecordId, IDictionary<string, object?> attributes)
    {
    }
    
}

public class RtEntity
{
    public RtEntity()
    {
        Attributes = new Dictionary<string, object?>();
    }
    
    public IDictionary<string, object?> Attributes { get; set; }

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
    
    public TValue GetAttributeValue<TValue>(string attributeName)
    {
        throw new InvalidOperationException();
    }

    public void SetAttributeValue(string attributeName, AttributeValueTypesDto attributeValueType, object? value)
    {
    }
    
    public void SetAttributeValueNonNullable(string attributeName, AttributeValueTypesDto attributeValueType, object value)
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
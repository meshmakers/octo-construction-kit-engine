using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Newtonsoft.Json;

namespace Meshmakers.Octo.Runtime.Contracts.Serialization;

/// <summary>
/// Default settings for Newtonsoft serializer using Rt Model
/// </summary>
public static class RtNewtonsoftSerializer
{
    /// <summary>
    /// Gets the default serializer for working with RT model
    /// </summary>
    public static readonly JsonSerializer DefaultSerializer = new()
    {
        DefaultValueHandling = DefaultValueHandling.Ignore,
        Converters =
        {
            new NewtonOctoObjectIdConverter(),
            new NewtonCkTypeIdConverter(),
            new NewtonCkEnumIdConverter(),
            new NewtonCkRecordIdConverter(),
            new NewtonCkAttributeIdConverter(),
            new NewtonCkAssociationRoleIdConverter()
        }
    };
}
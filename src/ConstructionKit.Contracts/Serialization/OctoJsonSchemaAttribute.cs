using System.Reflection;
using Json.Schema;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
/// Identifies a <see cref="T:Json.Schema.JsonSchema" /> to use when deserializing a type.
/// </summary>
/// <remarks>
/// This is a copy of the JsonSchemaAttribute from JsonSchema.Net library. It is copied because the original contains a bug
/// that results in a stack overflow when used with the ValidatingJsonConverter. This behavior is fixed and can be
/// reproduced using the original library by adding a schema to a type that references itself. Executing all Unit tests will
/// fail with NullReferenceExceptions.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public class OctoJsonSchemaAttribute : Attribute
{
    /// <summary>
    /// The schema to use when deserializing the type.
    /// </summary>
    public JsonSchema Schema { get; }

    /// <summary>
    /// Identifies a <see cref="T:Json.Schema.JsonSchema" /> to use when deserializing a type.
    /// </summary>
    /// <param name="declaringType">The type declaring the schema.</param>
    /// <param name="memberName">The property or field name for the schema.  This member must be public and static.</param>
    /// <exception cref="T:System.ArgumentException">Thrown when the member cannot be found or is not public and static.</exception>
    public OctoJsonSchemaAttribute(Type declaringType, string memberName)
    {
        MemberInfo? memberInfo = declaringType.GetProperty(memberName, BindingFlags.Static | BindingFlags.Public);
        if (memberInfo == null)
        {
            memberInfo = declaringType.GetField(memberName, BindingFlags.Static | BindingFlags.Public);
            if (memberInfo == null)
                throw new ArgumentException("Cannot find public static member named `" + memberName + "`");
        }
        PropertyInfo? propertyInfo = memberInfo as PropertyInfo;
        FieldInfo? fieldInfo = memberInfo as FieldInfo;
        if (!((propertyInfo?.GetValue(null) ?? fieldInfo?.GetValue(null)) is JsonSchema jsonSchema))
            throw new ArgumentException("Value of property must be `" + typeof (JsonSchema).FullName + "`");
        this.Schema = jsonSchema;
    }
}
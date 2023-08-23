using System.Reflection;
using Json.Schema;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
/// Identifies a <see cref="T:Json.Schema.JsonSchema" /> to use when deserializing a type.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public class OctoJsonSchemaAttribute : Attribute
{
    public JsonSchema Schema { get; }

    /// <summary>
    /// Identifies a <see cref="T:Json.Schema.JsonSchema" /> to use when deserializing a type.
    /// </summary>
    /// <param name="declaringType">The type declaring the schema.</param>
    /// <param name="memberName">The property or field name for the schema.  This member must be public and static.</param>
    /// <exception cref="T:System.ArgumentException">Thrown when the member cannot be found or is not public and static.</exception>
    public OctoJsonSchemaAttribute(Type declaringType, string memberName)
    {
        MemberInfo? memberInfo = (MemberInfo?) declaringType.GetProperty(memberName, BindingFlags.Static | BindingFlags.Public);
        if (memberInfo == (MemberInfo?) null)
        {
            memberInfo = (MemberInfo?) declaringType.GetField(memberName, BindingFlags.Static | BindingFlags.Public);
            if (memberInfo == (MemberInfo?) null)
                throw new ArgumentException("Cannot find public static member named `" + memberName + "`");
        }
        PropertyInfo? propertyInfo = memberInfo as PropertyInfo;
        FieldInfo? fieldInfo = memberInfo as FieldInfo;
        if (!((propertyInfo?.GetValue((object?) null) ?? fieldInfo?.GetValue((object?) null)) is JsonSchema jsonSchema))
            throw new ArgumentException("Value of property must be `" + typeof (JsonSchema).FullName + "`");
        this.Schema = jsonSchema;
    }
}
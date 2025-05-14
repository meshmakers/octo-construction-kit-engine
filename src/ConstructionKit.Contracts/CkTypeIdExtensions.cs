using Meshmakers.Common.Shared;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Extensions for <see cref="CkId{CkTypeId}"/>.
/// </summary>
public static class CkTypeIdExtensions
{
    /// <summary>
    /// Returns a string representation of the <see cref="CkId{CkTypeId}"/> that can be used as a type name.
    /// </summary>
    /// <param name="ckTypeId">The <see cref="CkId{CkTypeId}"/> to convert.</param>
    /// <returns></returns>
    public static string GetTypeName(this CkId<CkTypeId> ckTypeId)
    {
        return ckTypeId.SemanticVersionedFullName
            .Replace(".", "")
            .Replace("/", "")
            .ToCamelCase();
    }
}
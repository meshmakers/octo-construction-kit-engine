using Meshmakers.Common.Shared;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Extensions for <see cref="RtCkId{CkTypeId}"/>.
/// </summary>
public static class RtCkTypeIdExtensions
{
    /// <summary>
    /// Returns a string representation of the <see cref="CkId{CkTypeId}"/> that can be used as a type name.
    /// </summary>
    /// <param name="rtCkTypeId">The <see cref="CkId{CkTypeId}"/> to convert.</param>
    /// <returns></returns>
    public static string GetTypeName(this RtCkId<CkTypeId> rtCkTypeId)
    {
        return rtCkTypeId.SemanticVersionedFullName
            .Replace(".", "")
            .Replace("/", "")
            .ToCamelCase();
    }
}
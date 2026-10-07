namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Builds and parses qualified CK method ids (CK v2, AB#5669). A qualified method id is model-version-less:
///     <c>{declaringRtCkTypeId.SemanticVersionedFullName}.{methodId}</c>, e.g.
///     <c>System.Identity/User.ChangePassword-1</c>. It is the routing key of a method (queue names, registry).
/// </summary>
public static class CkMethodIds
{
    /// <summary>
    ///     Returns the qualified method id of <paramref name="methodId" /> declared on <paramref name="declaringCkTypeId" />.
    /// </summary>
    /// <param name="declaringCkTypeId">The runtime id of the CK type that declares the method</param>
    /// <param name="methodId">The element-versioned method id, e.g. <c>ChangePassword-1</c></param>
    public static string Qualify(RtCkId<CkTypeId> declaringCkTypeId, string methodId)
    {
        if (declaringCkTypeId == null || declaringCkTypeId.IsEmpty)
        {
            throw new ArgumentException("The declaring CK type id must not be empty.", nameof(declaringCkTypeId));
        }

        if (string.IsNullOrWhiteSpace(methodId))
        {
            throw new ArgumentException("The method id must not be empty.", nameof(methodId));
        }

        return $"{declaringCkTypeId.SemanticVersionedFullName}.{methodId}";
    }

    /// <summary>
    ///     Parses a qualified method id produced by <see cref="Qualify" />. The method id is the part after the
    ///     last '.' that follows the last '/', so dotted model and type names
    ///     (<c>System.Identity/Some.Type.Do-1</c>) parse correctly.
    /// </summary>
    /// <param name="qualifiedMethodId">The qualified method id</param>
    /// <param name="declaringCkTypeId">The runtime id of the declaring CK type</param>
    /// <param name="methodId">The element-versioned method id</param>
    /// <returns>True when the value is a well-formed qualified method id</returns>
    public static bool TryParse(string? qualifiedMethodId, out RtCkId<CkTypeId> declaringCkTypeId, out string methodId)
    {
        declaringCkTypeId = null!;
        methodId = null!;
        if (string.IsNullOrWhiteSpace(qualifiedMethodId))
        {
            return false;
        }

        var slash = qualifiedMethodId!.LastIndexOf('/');
        if (slash <= 0 || slash == qualifiedMethodId.Length - 1)
        {
            return false;
        }

        var dot = qualifiedMethodId.LastIndexOf('.');
        if (dot <= slash + 1 || dot == qualifiedMethodId.Length - 1)
        {
            return false;
        }

        try
        {
            declaringCkTypeId = new RtCkId<CkTypeId>(qualifiedMethodId.Substring(0, dot));
        }
        catch (ArgumentOutOfRangeException)
        {
            declaringCkTypeId = null!;
            return false;
        }

        methodId = qualifiedMethodId.Substring(dot + 1);
        return true;
    }
}

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Extensions to string
/// </summary>
public static class StringExtensions
{
    /// <summary>
    ///     Makes from a construction kit id a valid C# class name
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    public static string MakeClassName(this string s)
    {
        return s.Trim().Replace(".", "");
    }

    /// <summary>
    /// Makes from a construction kit type id a valid C# class name
    /// </summary>
    /// <param name="ckTypeId">The construction kit type id</param>
    /// <returns>A valid C# class name</returns>
    public static string MakeClassName(this CkTypeId ckTypeId)
    {
        string version = "";
        if (ckTypeId.Version.Major > 1)
        {
            version = $"{ckTypeId.Version.Major}";
        }


        return ckTypeId.Name.MakeClassName() + version;
    }

    /// <summary>
    /// Makes from a construction kit attribute id a valid C# class name
    /// </summary>
    /// <param name="ckAttributeId">The construction kit attribute id</param>
    /// <returns>A valid C# class name</returns>
    public static string MakeClassName(this CkAttributeId ckAttributeId)
    {
        string version = "";
        if (ckAttributeId.Version.Major > 1)
        {
            version = $"{ckAttributeId.Version.Major}";
        }

        return ckAttributeId.Name.MakeClassName() + version;
    }

    /// <summary>
    /// Makes from a construction kit association role id a valid C# class name
    /// </summary>
    /// <param name="ckAssociationRoleId">The construction kit association role id</param>
    /// <returns>A valid C# class name</returns>
    public static string MakeClassName(this CkAssociationRoleId ckAssociationRoleId)
    {
        string version = "";
        if (ckAssociationRoleId.Version.Major > 1)
        {
            version = $"{ckAssociationRoleId.Version.Major}";
        }

        return ckAssociationRoleId.RoleId.MakeClassName() + version;
    }

    /// <summary>
    /// Makes from a construction kit enum id a valid C# class name
    /// </summary>
    /// <param name="ckEnumId">The construction kit enum id</param>
    /// <returns>A valid C# class name</returns>
    public static string MakeClassName(this CkEnumId ckEnumId)
    {
        string version = "";
        if (ckEnumId.Version.Major > 1)
        {
            version = $"{ckEnumId.Version.Major}";
        }

        return ckEnumId.Name.MakeClassName() + version;
    }

    /// <summary>
    /// Makes from a construction kit record id a valid C# class name
    /// </summary>
    /// <param name="ckRecordId">The construction kit record id</param>
    /// <returns>A valid C# class name</returns>
    public static string MakeClassName(this CkRecordId ckRecordId)
    {
        string version = "";
        if (ckRecordId.Version.Major > 1)
        {
            version = $"{ckRecordId.Version.Major}";
        }

        return ckRecordId.Name.MakeClassName() + version;
    }
}
using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

/// <summary>
///     How the archives a blueprint seed creates are treated on install and update (AB#7991). By default every archive
///     the seed newly creates on a tenant is activated and every new rollup is backfilled from its sources; archives that
///     already exist on the tenant are never touched.
/// </summary>
public class BlueprintArchiveActivationDto
{
    /// <summary>
    ///     Archives of the seed that are not activated when the seed creates them, by runtime id (<c>rtId</c>) or
    ///     well-known name (<c>rtWellKnownName</c>). They stay <c>Created</c> until someone activates them.
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public List<string>? Exclude { get; set; }
}

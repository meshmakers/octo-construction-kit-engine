namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

/// <summary>
///     Strongly-typed dictionary used for the <c>requires:</c> block in
///     <see cref="BlueprintMetaRootDto"/>. Each key is a blueprint-variable name
///     (e.g. <c>octo.environment</c>) and the value is the list of acceptable values.
///     The blueprint is applied to a tenant only when every key resolves to a value
///     present in the corresponding list.
/// </summary>
/// <remarks>
///     A dedicated subclass exists purely so the YAML round-trip converter
///     (<c>RequiresMapConverter</c>) can be registered against this exact type without
///     hijacking unrelated <c>Dictionary&lt;string, List&lt;string&gt;&gt;</c> usages.
///     The converter accepts both scalar (<c>octo.isSystemTenant: "true"</c>) and
///     sequence (<c>octo.environment: [staging, production]</c>) value shapes when
///     deserialising; serialisation always emits sequences for stability.
/// </remarks>
public sealed class RequiresMap : Dictionary<string, List<string>>
{
}

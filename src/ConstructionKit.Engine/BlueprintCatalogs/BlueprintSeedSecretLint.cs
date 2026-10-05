using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using YamlDotNet.RepresentationModel;

namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
///     Blueprint seed lint for Secret attributes (AB#5528, decision 9): a seed may carry only an
///     empty value or a <c>&lt;placeholder&gt;</c> for a Secret attribute - secrets are set after
///     installation (Studio, CLI, rotate endpoints), never shipped in a blueprint (AB#5529).
/// </summary>
/// <remarks>
///     Works on the YAML representation model like the rtId check of
///     <see cref="BlueprintCompilerService" />: the runtime-model DTOs live in Runtime.Contracts, which
///     the CK engine does not reference. Attribute ids are compared by
///     <see cref="NormaliseAttributeId(string?)" />. Record values (<c>ckRecordId</c> + <c>attributes</c>) are
///     walked recursively, so Secret sub-attributes of records are covered too.
/// </remarks>
public static class BlueprintSeedSecretLint
{
    /// <summary>
    ///     Message number of a seed that carries a value for a Secret attribute (error).
    /// </summary>
    public const int SecretSeedValueMessageNumber = 10;

    /// <summary>
    ///     Message number of a lint that could not resolve a CK model (warning).
    /// </summary>
    public const int UnresolvedModelMessageNumber = 11;

    /// <summary>
    ///     Normalised attribute id <c>Model/Attribute-Version</c>: a model version suffix
    ///     (<c>Model-1.2.3/Attr</c>) is dropped and a missing attribute version becomes <c>-1</c>, so seed
    ///     ids (<c>System.Communication/Password</c>) and compiled ids compare equal.
    /// </summary>
    /// <param name="attributeId">An attribute id as written in a seed</param>
    /// <returns>The normalised id, or <c>null</c> when the id has no model part</returns>
    public static string? NormaliseAttributeId(string? attributeId)
    {
        if (string.IsNullOrWhiteSpace(attributeId))
        {
            return null;
        }

        var separator = attributeId!.LastIndexOf('/');
        if (separator <= 0 || separator == attributeId.Length - 1)
        {
            return null;
        }

        var modelPart = attributeId.Substring(0, separator).Trim();
        var versionIndex = modelPart.IndexOf('-');
        if (versionIndex > 0)
        {
            modelPart = modelPart.Substring(0, versionIndex);
        }

        var attributePart = attributeId.Substring(separator + 1).Trim();
        if (attributePart.IndexOf('-') < 0)
        {
            attributePart += "-1";
        }

        return $"{modelPart}/{attributePart}";
    }

    /// <summary>
    ///     Normalised attribute id of a compiled attribute definition.
    /// </summary>
    public static string NormaliseAttributeId(string modelName, CkAttributeId attributeId)
    {
        return $"{modelName}/{attributeId.Name}-{attributeId.Version}";
    }

    /// <summary>
    ///     Reads the <c>dependencies</c> list of a seed (runtime model) file.
    /// </summary>
    /// <param name="seedYaml">The parsed seed</param>
    /// <returns>The declared dependency ranges</returns>
    public static IReadOnlyList<CkModelIdVersionRange> ReadDependencies(YamlStream seedYaml)
    {
        var result = new List<CkModelIdVersionRange>();
        if (seedYaml.Documents.Count == 0 || seedYaml.Documents[0].RootNode is not YamlMappingNode root ||
            !root.Children.TryGetValue(new YamlScalarNode("dependencies"), out var node) ||
            node is not YamlSequenceNode dependencies)
        {
            return result;
        }

        foreach (var dependency in dependencies.Children.OfType<YamlScalarNode>())
        {
            if (!string.IsNullOrWhiteSpace(dependency.Value))
            {
                result.Add(new CkModelIdVersionRange(dependency.Value!.Trim()));
            }
        }

        return result;
    }

    /// <summary>
    ///     Lints one seed file.
    /// </summary>
    /// <param name="seedYaml">The parsed seed</param>
    /// <param name="seedDataPath">Path of the seed for messages</param>
    /// <param name="secretAttributeIds">Normalised ids (<see cref="NormaliseAttributeId(string?)" />) of Secret attributes</param>
    /// <param name="operationResult">Receives one error per offending value</param>
    /// <returns>Number of offending values</returns>
    public static int Lint(YamlStream seedYaml, string seedDataPath, ISet<string> secretAttributeIds,
        OperationResult operationResult)
    {
        if (secretAttributeIds.Count == 0 || seedYaml.Documents.Count == 0 ||
            seedYaml.Documents[0].RootNode is not YamlMappingNode root ||
            !root.Children.TryGetValue(new YamlScalarNode("entities"), out var entitiesNode) ||
            entitiesNode is not YamlSequenceNode entities)
        {
            return 0;
        }

        var violations = 0;
        foreach (var entity in entities.Children.OfType<YamlMappingNode>())
        {
            var identity = $"{Scalar(entity, "ckTypeId")}@{Scalar(entity, "rtId")}";
            violations += LintAttributes(entity, identity, seedDataPath, secretAttributeIds, operationResult);
        }

        return violations;
    }

    private static int LintAttributes(YamlMappingNode holder, string identity, string seedDataPath,
        ISet<string> secretAttributeIds, OperationResult operationResult)
    {
        if (!holder.Children.TryGetValue(new YamlScalarNode("attributes"), out var attributesNode) ||
            attributesNode is not YamlSequenceNode attributes)
        {
            return 0;
        }

        var violations = 0;
        foreach (var attribute in attributes.Children.OfType<YamlMappingNode>())
        {
            var attributeId = Scalar(attribute, "id");
            attribute.Children.TryGetValue(new YamlScalarNode("value"), out var valueNode);

            var normalisedId = NormaliseAttributeId(attributeId);
            if (normalisedId != null && secretAttributeIds.Contains(normalisedId))
            {
                if (!IsAllowedSecretSeedValue(valueNode))
                {
                    violations++;
                    // Never echo the value - it is a credential.
                    operationResult.AddMessage(new OperationMessage(
                        MessageLevel.Error,
                        seedDataPath,
                        SecretSeedValueMessageNumber,
                        $"Seed entity '{identity}' sets Secret attribute '{attributeId}' to a value. Seeds may only "
                        + "contain an empty value or a '<placeholder>' for Secret attributes; set the secret after "
                        + "installation (Studio, octo-cli) and rotate any credential that was committed."));
                }

                continue;
            }

            // Records and record arrays: walk their attributes.
            switch (valueNode)
            {
                case YamlMappingNode record:
                    violations += LintAttributes(record, identity, seedDataPath, secretAttributeIds, operationResult);
                    break;
                case YamlSequenceNode items:
                    foreach (var item in items.Children.OfType<YamlMappingNode>())
                    {
                        violations += LintAttributes(item, identity, seedDataPath, secretAttributeIds,
                            operationResult);
                    }

                    break;
            }
        }

        return violations;
    }

    private static bool IsAllowedSecretSeedValue(YamlNode? valueNode)
    {
        switch (valueNode)
        {
            case null:
                return true;
            case YamlScalarNode scalar:
                if (scalar.Style == YamlDotNet.Core.ScalarStyle.Plain &&
                    (scalar.Value is null or "" or "~" or "null" or "Null" or "NULL"))
                {
                    return true;
                }

                return SecretAttributeConventions.IsAllowedSeedValue(scalar.Value);
            default:
                return false;
        }
    }

    private static string Scalar(YamlMappingNode node, string key)
    {
        return node.Children.TryGetValue(new YamlScalarNode(key), out var value) && value is YamlScalarNode scalar
            ? scalar.Value ?? string.Empty
            : string.Empty;
    }
}

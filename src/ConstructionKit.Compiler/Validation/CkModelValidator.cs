using System.Text.RegularExpressions;
using Meshmakers.Octo.ConstructionKit.Compiler.Messages;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Resolvers;
using Meshmakers.Octo.ConstructionKit.Contracts.Validation;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Validation;

public class CkModelValidator : ICkModelValidator
{
    private readonly IDependencyResolver _dependencyResolver;
    private readonly IInheritanceResolver _inheritanceResolver;
    private readonly IElementResolver _elementResolver;

    public CkModelValidator(IDependencyResolver dependencyResolver, IInheritanceResolver inheritanceResolver,
        IElementResolver elementResolver)
    {
        _dependencyResolver = dependencyResolver;
        _inheritanceResolver = inheritanceResolver;
        _elementResolver = elementResolver;
    }

    public async Task ValidateAsync(CkCompiledModelRoot compiledModel, OperationResult operationResult)
    {

        // By creating the model graph, a validation is done if association roles, attributes and entities are unique.
        CkModelGraph modelGraph = _elementResolver.Resolve(compiledModel, operationResult);
        
        if (!Regex.IsMatch(compiledModel.ModelId.ModelId, CompilerStatics.AllowedCharactersInNamesRegex))
        {
            operationResult.AddMessage(MessageCodes.ModelIdContainsInvalidCharacters(compiledModel.ModelId.ModelId));
            throw ModelValidationException.ModelIdContainsInvalidCharacters(compiledModel.ModelId.ModelId);
        }

        // Before the checks, we need to build a cache of the model.
        // We check if the can retrieve the model from one of the model repository sources (e.g. database).
        // We combine all entities, attributes and association roles into one list.
        CkAggregatedModelElements aggregatedModelElements = new();

        if (compiledModel.Dependencies != null)
        {
            aggregatedModelElements = await _dependencyResolver.ResolveDependenciesAsync(compiledModel.Dependencies, operationResult);
        }

        // We suppose that the dependent models are already validated and we can use them.
        // So we check the current to be validated model against the dependent models.

        // Check: Ensure that the model forces no circular dependencies.
        if (aggregatedModelElements.CkModelDependencies.Any(x => x.Key.ModelId == compiledModel.ModelId.ModelId))
        {
            var dependentModels = aggregatedModelElements.CkModelDependencies.Keys.Where(x => x.ModelId == compiledModel.ModelId.ModelId);

            operationResult.AddMessage(
                MessageCodes.CircularDependency(compiledModel.ModelId.ModelId, dependentModels.Select(x => x.ModelId).ToList()));
        }

        // Check: There are only a few places, where elements of other models are used.
        // 1. entities.attributes.id -> Reference to a defined attribute.
        // 2. entities.ckDerivedId -> Reference to a defined type.
        // 3. entities.associations.roleId -> Reference to a defined association role.
        // 4. entities.associations.targetCkTypeId -> Reference to a defined type.
        ValidateReferences(aggregatedModelElements, modelGraph, operationResult);

        // Check: Inheritance.
        // 1. entities.ckDerivedId -> Only one type cannot have a derived type: System.Entity.
        // 2. entities.attributes -> It is not possible that a type has an attribute, which is defined in a base type.
        // 3. entities.attributes -> It is not possible that a type has an attribute name, that is defined in a base type.
        // 4. entities.associations -> It is not possible that a type has an association, which is defined in a base type too.
        // 5. entities.isFinal -> It is not possible that a type is final, but has a derived type.
        
        // Check 1-5 is done by inheritance resolver.
        _inheritanceResolver.Resolve(aggregatedModelElements, modelGraph, operationResult);
    }

    private static void ValidateReferences(CkAggregatedModelElements aggregatedModelElements, CkModelGraph modelGraph,
        OperationResult operationResult)
    {
        foreach (var ckTypeKeyValue in aggregatedModelElements.CkTypes)
        {
            // Check 1.
            if (ckTypeKeyValue.Value.Attributes != null)
            {
                foreach (var ckTypeAttribute in ckTypeKeyValue.Value.Attributes)
                {
                    if (!aggregatedModelElements.CkAttributes.ContainsKey(ckTypeAttribute.CkAttributeId) &&
                        !modelGraph.Attributes.ContainsKey(ckTypeAttribute.CkAttributeId))
                    {
                        operationResult.AddMessage(
                            MessageCodes.UnknownAttributeOfCkTypeIdInSource(ckTypeKeyValue.Key, ckTypeAttribute.CkAttributeId));
                    }
                }
            }

            // Check 2.
            if (ckTypeKeyValue.Value.DerivedFromCkTypeId != null)
            {
                if (!aggregatedModelElements.CkTypes.ContainsKey(ckTypeKeyValue.Value.DerivedFromCkTypeId.Value) &&
                    !modelGraph.Types.ContainsKey(ckTypeKeyValue.Value.DerivedFromCkTypeId.Value))
                {
                    operationResult.AddMessage(
                        MessageCodes.UnknownCkDerivedIdOfCkTypeIdInSource(ckTypeKeyValue.Value.DerivedFromCkTypeId,
                            ckTypeKeyValue.Key));
                }
            }

            if (ckTypeKeyValue.Value.Associations != null)
            {
                foreach (var ckTypeAssociation in ckTypeKeyValue.Value.Associations)
                {
                    // Check 3.
                    if (!aggregatedModelElements.CkAssociationRoles.ContainsKey(ckTypeAssociation.CkRoleId) &&
                        !modelGraph.AssociationRoles.ContainsKey(ckTypeAssociation.CkRoleId))
                    {
                        operationResult.AddMessage(
                            MessageCodes.UnknownAssociationRoleOfCkTypeIdInSource(ckTypeKeyValue.Key, ckTypeAssociation.CkRoleId));
                    }

                    // Check 4.
                    if (!aggregatedModelElements.CkTypes.ContainsKey(ckTypeAssociation.TargetCkTypeId) &&
                        !modelGraph.Types.ContainsKey(ckTypeAssociation.TargetCkTypeId))
                    {
                        operationResult.AddMessage(
                            MessageCodes.UnknownTargetCkTypeIdOfCkTypeIdInSource(ckTypeKeyValue.Key, ckTypeAssociation.TargetCkTypeId));
                    }
                }
            }
        }
    }
}
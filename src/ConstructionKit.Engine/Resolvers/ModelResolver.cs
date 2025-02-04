using System.Text.RegularExpressions;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
///     Resolver that resolves the elements of a compiled model.
/// </summary>
internal class ModelResolver : IModelResolver
{
    private readonly IDependencyResolver _dependencyResolver;
    private readonly IElementResolver _elementResolver;
    private readonly IInheritanceResolver _inheritanceResolver;
    private readonly IReferenceResolver _referenceResolver;
    private readonly IVariableResolver _variableResolver;

    /// <summary>
    ///     Creates a new instance of <see cref="ModelResolver" />.
    /// </summary>
    /// <param name="dependencyResolver"></param>
    /// <param name="inheritanceResolver"></param>
    /// <param name="elementResolver"></param>
    /// <param name="referenceResolver"></param>
    /// <param name="variableResolver"></param>
    public ModelResolver(IDependencyResolver dependencyResolver, IInheritanceResolver inheritanceResolver,
        IElementResolver elementResolver, IReferenceResolver referenceResolver, IVariableResolver variableResolver)
    {
        _dependencyResolver = dependencyResolver;
        _inheritanceResolver = inheritanceResolver;
        _elementResolver = elementResolver;
        _referenceResolver = referenceResolver;
        _variableResolver = variableResolver;
    }

    public Task<CkModelGraph> ResolveAsync(ICollection<CkModelId> ckModelIds, OperationResult operationResult, object? sourceIdentifier = null)
    {
        var originFileResolver = new OriginFileResolver("-");
        return ResolveAsync(ckModelIds, originFileResolver, operationResult, sourceIdentifier);
    }

    public async Task<CkModelGraph> ResolveAsync(ICollection<CkModelId> ckModelIds, IOriginFileResolver originFileResolver,
        OperationResult operationResult, object? sourceIdentifier = null)
    {
        var modelGraph = new CkModelGraph();
        await _dependencyResolver.ResolveDependenciesAsync(ckModelIds, modelGraph, originFileResolver, operationResult, sourceIdentifier)
            .ConfigureAwait(false);

        _referenceResolver.Resolve(modelGraph, originFileResolver, operationResult);
        _inheritanceResolver.Resolve(modelGraph, originFileResolver, operationResult);

        return modelGraph;
    }

    public async Task<CkModelGraph> ResolveAsync(CkCompiledModelRoot compiledModel, IOriginFileResolver originFileResolver,
        OperationResult operationResult, object? sourceIdentifier = null)
    {
        _variableResolver.SetVariable("thisModel", compiledModel.ModelId.FullName);
        
        // By creating the model graph, a validation is done if association roles, attributes and entities are unique.
        var modelGraph = _elementResolver.Resolve(compiledModel, _variableResolver, originFileResolver, operationResult);

        if (!Regex.IsMatch(compiledModel.ModelId.ModelId, CompilerStatics.AllowedCharactersInNamesRegex))
        {
            operationResult.AddMessage(MessageCodes.ModelIdContainsInvalidCharacters(
                originFileResolver.Resolve(compiledModel.ModelId.ModelId), compiledModel.ModelId.ModelId));
            throw ModelValidationException.ModelIdContainsInvalidCharacters(compiledModel.ModelId.ModelId);
        }

        // Before the checks, we need to build a cache of the model.
        // We check if they can retrieve the model from one of the model repository sources (e.g., database).
        // We combine all entities, attributes and association roles into one list.
        if (compiledModel.Dependencies != null)
        {
            await _dependencyResolver.ResolveDependenciesAsync(compiledModel.Dependencies, modelGraph, originFileResolver, 
                    operationResult, sourceIdentifier)
                .ConfigureAwait(false);
        }

        // We suppose that the dependent models are already validated and we can use them.
        // So we check the current to be validated model against the dependent models.

        // Check: Ensure that the model forces no circular dependencies.
        if (modelGraph.Dependencies.Any(x => x.Key.ModelId == compiledModel.ModelId.ModelId))
        {
            var dependentModels = modelGraph.Dependencies.Keys.Where(x => x.ModelId == compiledModel.ModelId.ModelId);

            operationResult.AddMessage(MessageCodes.CircularDependency(originFileResolver.Resolve(compiledModel.ModelId.ModelId),
                compiledModel.ModelId.ModelId, dependentModels.Select(x => x.ModelId).ToList()));
        }

        // Check: There are only a few places, where elements of other models are used.
        // 1. entities.attributes.id -> Reference to a defined attribute.
        // 2. entities.ckDerivedId -> Reference to a defined type.
        // 3. entities.associations.roleId -> Reference to a defined association role.
        // 4. entities.associations.targetCkTypeId -> Reference to a defined type.
        _referenceResolver.Resolve(modelGraph, originFileResolver, operationResult);

        // Check: Inheritance.
        // 1. entities.ckDerivedId -> Only one type cannot have a derived type: System.Entity.
        // 2. entities.attributes -> It is not possible that a type has an attribute, which is defined in a base type.
        // 3. entities.attributes -> It is not possible that a type has an attribute name, that is defined in a base type.
        // 4. entities.associations -> It is not possible that a type has an association, which is defined in a base type too.
        // 5. entities.isFinal -> It is not possible that a type is final, but has a derived type.

        // Check 1-5 is done by inheritance resolver.
        _inheritanceResolver.Resolve(modelGraph, originFileResolver, operationResult);

        return modelGraph;
    }
}
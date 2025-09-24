using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Meshmakers.Octo.ConstructionKit.Contracts.Services;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

namespace Meshmakers.Octo.ConstructionKit.Engine.Services;

/// <summary>
///     Implementation of <see cref="ICkValidationService" /> that validates a compiled model.
/// </summary>
public class CkValidationService : ICkValidationService
{
    private readonly IModelResolver _modelResolver;

    /// <summary>
    ///     Creates a new instance of the <see cref="CkValidationService" /> class.
    /// </summary>
    /// <param name="modelResolver"></param>
    public CkValidationService(IModelResolver modelResolver)
    {
        _modelResolver = modelResolver;
    }

    /// <inheritdoc />
    public async Task<ICkModelGraph> ValidateAsync(CkCompiledModelRoot compiledModel,
        IOriginFileResolver originFileResolver, OperationResult operationResult, object? sourceIdentifier = null)
    {
        return await _modelResolver.ResolveAsync(compiledModel, originFileResolver, operationResult, sourceIdentifier)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<(ICkModelGraph, CkCompiledModelRoot)> ValidateAsync(CkModelCompileCandidate compileCandidate, IOriginFileResolver originFileResolver,
        OperationResult operationResult, object? sourceIdentifier = null)
    {
        return await _modelResolver.ResolveAsync(compileCandidate, originFileResolver, operationResult, sourceIdentifier)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ICkModelGraph> ValidateAsync(CkCompiledModelRoot compiledModel, OperationResult operationResult,
        object? sourceIdentifier = null)
    {
        var originFileResolver = new OriginFileResolver("-");
        return await _modelResolver.ResolveAsync(compiledModel, originFileResolver, operationResult, sourceIdentifier)
            .ConfigureAwait(false);
    }
}
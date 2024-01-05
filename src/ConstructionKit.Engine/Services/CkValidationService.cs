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
    public async Task<CkModelGraph> ValidateAsync(CkCompiledModelRoot compiledModel, OperationResult operationResult)
    {
        return await _modelResolver.ResolveAsync(compiledModel, operationResult).ConfigureAwait(false);
    }
}
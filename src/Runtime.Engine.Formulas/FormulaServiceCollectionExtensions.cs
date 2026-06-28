using Meshmakers.Octo.Runtime.Contracts.Formulas;
using Meshmakers.Octo.Runtime.Engine.Formulas;
using Microsoft.Extensions.DependencyInjection.Extensions;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registration for the shared mXparser formula engine.
/// </summary>
public static class FormulaServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IFormulaEngine"/> (singleton, stateless). Idempotent.
    /// </summary>
    public static IServiceCollection AddFormulaEngine(this IServiceCollection services)
    {
        services.TryAddSingleton<IFormulaEngine, FormulaEngine>();
        return services;
    }
}

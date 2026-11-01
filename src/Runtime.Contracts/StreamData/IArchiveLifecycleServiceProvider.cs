using System.Threading.Tasks;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Resolves the tenant-scoped <see cref="IArchiveLifecycleService"/> for code that runs outside a tenant context, such
/// as the runtime import (AB#7997): when a seed adds a column to an activated archive, the import hands the addition to
/// the lifecycle service instead of writing the column definition without its storage. The storage backend registers
/// the implementation; without one the import refuses such an addition.
/// </summary>
public interface IArchiveLifecycleServiceProvider
{
    /// <summary>
    /// Returns the archive lifecycle service of <paramref name="tenantId"/>, or <c>null</c> when the tenant is unknown
    /// or has stream data switched off.
    /// </summary>
    Task<IArchiveLifecycleService?> GetAsync(string tenantId);
}

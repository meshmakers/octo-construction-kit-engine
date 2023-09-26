using Meshmakers.Octo.ConstructionKit.Contracts;

// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

/// <summary>
/// Represents a runtime record, the instance of a construction kit record
/// </summary>
public class RtRecord : RtTypeWithAttributes
{
    /// <summary>
    ///     Gets or sets the construction kit id
    /// </summary>
    public CkId<CkRecordId> CkRecordId { get; set; }

    /// <inheritdoc />
    protected override string GetLocation()
    {
        return $"{CkRecordId}";
    }
}
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
/// Represents query options for runtime associations.
/// </summary>
public record RtAssociationQueryOptions
{
    /// <summary>
    /// Gets the graph direction of the association.
    /// </summary>
    public GraphDirections Direction { get; }

    /// <summary>
    /// Gets if defined a value indicating how many associations are skipped
    /// </summary>
    public int? Skip { get; }

    /// <summary>
    /// Gets if defined a value indicating how many associations are taken.
    /// </summary>
    public int? Take { get; }

    private RtAssociationQueryOptions(GraphDirections direction, int? skip = null, int? take = null)
    {
        Direction = direction;
        Skip = skip;
        Take = take;
    }

    /// <summary>
    ///     Represents global filter settings for the query.
    /// </summary>
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public GlobalRtAssociationFilter? GlobalFilter { get; private set; }

    /// <summary>
    /// Defines global filter settings for runtime association queries
    /// </summary>
    /// <param name="includeArchived">When true, archived associations are returned by the data operation, otherwise not</param>
    public RtAssociationQueryOptions Global(bool includeArchived)
    {
        GlobalFilter = new GlobalRtAssociationFilter(includeArchived);

        return this;
    }

    /// <summary>
    ///     Creates a new instance of <see cref="RtAssociationQueryOptions" />.
    /// </summary>
    /// <param name="direction">The graph direction for query</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="take">Number of items to take</param>
    /// <returns></returns>
    public static RtAssociationQueryOptions Create(GraphDirections direction, int? skip = null, int? take = null)
    {
        return new RtAssociationQueryOptions(direction, skip, take);
    }
}
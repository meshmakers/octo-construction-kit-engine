using Meshmakers.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
/// Pair of rt association roles id and direction.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public record NavigationPair : FieldFilterCriteria
{
    /// <summary>
    /// Gets the navigation pairs used to further traverse the object graph.
    /// </summary>
    public List<NavigationPair> InnerNavigationPairs { get; }

    /// <summary>
    /// Gets the association role id.
    /// </summary>
    public CkId<CkAssociationRoleId> CkRoleId { get; }

    /// <summary>
    /// Gets the direction of the association.
    /// </summary>
    public GraphDirections Direction { get; }

    /// <summary>
    /// Gets the target construction kit type id.
    /// </summary>
    public CkId<CkTypeId> TargetCkTypeId { get; }

    /// <summary>
    /// Gets the path terms to the navigation pair.
    /// </summary>
    public IEnumerable<PathTerm> PathTerms { get; }

    /// <summary>
    /// Gets the sub path terms of the navigation pair. This property lists
    /// all sub paths of the navigation pair. The sub path terms are used to
    /// </summary>
    public IEnumerable<IEnumerable<PathTerm>> SubPathTerms { get; private set; }

    /// <summary>
    ///     Creates a new <see cref="NavigationPair" /> from the given <paramref name="ckRoleId" />, <paramref name="direction" />, and <paramref name="targetCkTypeId" />.
    /// </summary>
    /// <param name="pathTerms">Path terms to the navigation pair</param>
    /// <param name="subPathTerms">Sub path terms to the navigation pair</param>
    /// <param name="ckRoleId">Association role id</param>
    /// <param name="direction">Direction of the association</param>
    /// <param name="targetCkTypeId">Target construction kit type id</param>
    public NavigationPair(
        IEnumerable<PathTerm> pathTerms,
        IEnumerable<IEnumerable<PathTerm>> subPathTerms,
        CkId<CkAssociationRoleId> ckRoleId,
        GraphDirections direction,
        CkId<CkTypeId> targetCkTypeId)
    {
        PathTerms = pathTerms;
        SubPathTerms = subPathTerms;
        CkRoleId = ckRoleId;
        Direction = direction;
        TargetCkTypeId = targetCkTypeId;
        InnerNavigationPairs = new List<NavigationPair>();
    }

    /// <summary>
    ///     Creates a new <see cref="NavigationPair" /> from the given <paramref name="ckRoleId" />, <paramref name="direction" />, and <paramref name="targetCkTypeId" />.
    /// </summary>
    /// <param name="pathTerms">Path terms to the navigation pair</param>
    /// <param name="subPathTerms">Sub path terms to the navigation pair</param>
    /// <param name="ckRoleId">Association role id</param>
    /// <param name="direction">Direction of the association</param>
    /// <param name="targetCkTypeId">Target construction kit type id</param>
    /// <param name="innerNavigationPairs">Navigation pairs used to further traverse the object graph</param>
    public NavigationPair(
        IEnumerable<PathTerm> pathTerms,
        IEnumerable<IEnumerable<PathTerm>> subPathTerms,
        CkId<CkAssociationRoleId> ckRoleId,
        GraphDirections direction,
        CkId<CkTypeId> targetCkTypeId,
        IEnumerable<NavigationPair> innerNavigationPairs)
        : this(pathTerms, subPathTerms, ckRoleId, direction, targetCkTypeId)
    {
        InnerNavigationPairs = new List<NavigationPair>(innerNavigationPairs);
    }

    /// <summary>
    /// Merges the given <paramref name="other" /> navigation pair into this one by adding the inner navigation pairs of the other navigation pair to this one.
    /// </summary>
    /// <param name="other">The other navigation pair to merge</param>
    public void Merge(NavigationPair other)
    {
        ArgumentValidation.Validate(nameof(other), other);

        if (other.CkRoleId != CkRoleId || other.Direction != Direction || other.TargetCkTypeId != TargetCkTypeId)
        {
            throw new InvalidOperationException("Cannot merge navigation pairs with different properties.");
        }

        foreach (var otherInnerNavigationPair in other.InnerNavigationPairs)
        {
            var innerNavigationPair = InnerNavigationPairs.SingleOrDefault(x =>
                x.CkRoleId == otherInnerNavigationPair.CkRoleId && x.Direction == otherInnerNavigationPair.Direction &&
                x.TargetCkTypeId == otherInnerNavigationPair.TargetCkTypeId);

            if (innerNavigationPair == null)
            {
                InnerNavigationPairs.Add(otherInnerNavigationPair);
            }
            else
            {
                innerNavigationPair.Merge(otherInnerNavigationPair);
            }
        }


        SubPathTerms = SubPathTerms.Union(other.SubPathTerms);
    }
}
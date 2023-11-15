namespace Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

internal interface IAttributeValueList
{
    IList<RtRecord> InnerList { get; }
}
using Meshmakers.Octo.SystematizedData.Persistence.DatabaseEntities;

namespace Meshmakers.Octo.SystematizedData.Persistence.CkModel.CkRuleEngine;

public class GraphRuleEngineResult
{
    public GraphRuleEngineResult()
    {
        RtAssociationsToCreate = new List<RtAssociation>();
        RtAssociationsToDelete = new List<RtAssociation>();
    }

    public List<RtAssociation> RtAssociationsToCreate { get; }
    public List<RtAssociation> RtAssociationsToDelete { get; }
}

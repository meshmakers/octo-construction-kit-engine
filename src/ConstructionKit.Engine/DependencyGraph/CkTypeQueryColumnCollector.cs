using Meshmakers.Common.Shared;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.DependencyGraph;

internal class CkTypeQueryColumnCollector(CkModelGraph ckModelGraph)
{
    private const string Array = "[{0}]";
    private const string FirstElement = "0";
    private const string LastElement = "-1";
    private const string AllElements = "*";
    private const string Separator = ".";
    private const string SystemAttributeRtId = "RtId";
    private const string SystemAttributeRtWellKnownName = "RtWellKnownName";
    private const string SystemAttributeRtVersion = "RtVersion";
    private const string SystemAttributeRtCreationDateTime = "RtCreationDateTime";
    private const string SystemAttributeRtChangedDateTime = "RtChangedDateTime";

    public List<CkTypeQueryColumn> GetColumns(CkId<CkTypeId> ckTypeId)
    {
        var columns = new List<CkTypeQueryColumn>();

        if (!ckModelGraph.Types.TryGetValue(ckTypeId, out var type))
        {
            throw DependencyGraphException.CkTypeIdNotFound(ckTypeId);
        }

        CollectColumns(type, columns);

        columns.Add(new CkTypeQueryColumn(SystemAttributeRtId.ToCamelCase(), [new(SystemAttributeRtId, PathType.Attribute)],
            AttributeValueTypesDto.String));
        columns.Add(new CkTypeQueryColumn(SystemAttributeRtWellKnownName.ToCamelCase(), [new(SystemAttributeRtWellKnownName, PathType.Attribute)],
            AttributeValueTypesDto.String));
        columns.Add(new CkTypeQueryColumn(SystemAttributeRtVersion.ToCamelCase(), [new(SystemAttributeRtVersion, PathType.Attribute)],
            AttributeValueTypesDto.Int64));
        columns.Add(new CkTypeQueryColumn(SystemAttributeRtCreationDateTime.ToCamelCase(), [new(SystemAttributeRtCreationDateTime, PathType.Attribute)],
            AttributeValueTypesDto.DateTime));
        columns.Add(new CkTypeQueryColumn(SystemAttributeRtChangedDateTime.ToCamelCase(), [new(SystemAttributeRtChangedDateTime, PathType.Attribute)],
            AttributeValueTypesDto.DateTime));
        return columns;
    }


    private void CollectColumns(CkTypeWithAttributesGraph current, List<CkTypeQueryColumn> columns)
    {
        foreach (var attribute in current.AllAttributes.Values)
        {
            if (!ckModelGraph.Attributes.TryGetValue(attribute.CkAttributeId, out var attributeGraph))
            {
                throw DependencyGraphException.AttributeNotFound(attribute.CkAttributeId);
            }

            var attributeNamePascalCase = attribute.AttributeName;
            var attributeNameCamelCase = attribute.AttributeName.ToCamelCase();

            switch (attributeGraph.ValueType)
            {
                case AttributeValueTypesDto.Record:
                    if (attributeGraph.ValueCkRecordId == null)
                    {
                        throw DependencyGraphException.CkRecordIdNotDefined(attributeGraph.CkAttributeId);
                    }

                    if (!ckModelGraph.Records.TryGetValue(attributeGraph.ValueCkRecordId, out var recordGraph))
                    {
                        throw DependencyGraphException.RecordNotFound(attributeGraph.ValueCkRecordId);
                    }

                    var recordColumns = new List<CkTypeQueryColumn>();
                    CollectColumns(recordGraph, recordColumns);
                    columns.AddRange(recordColumns.Select(c =>
                    {
                        var l = c.AccessPathList.ToList();
                        l.Insert(0, new (attributeNamePascalCase, PathType.Attribute));
                        return new CkTypeQueryColumn(attributeNameCamelCase + Separator + c.Path, l, c.ValueType);
                    }));
                    break;
                case AttributeValueTypesDto.RecordArray:
                    if (attributeGraph.ValueCkRecordId == null)
                    {
                        throw DependencyGraphException.CkRecordIdNotDefined(attributeGraph.CkAttributeId);
                    }

                    if (!ckModelGraph.Records.TryGetValue(attributeGraph.ValueCkRecordId, out recordGraph))
                    {
                        throw DependencyGraphException.RecordNotFound(attributeGraph.ValueCkRecordId);
                    }

                    recordColumns = new List<CkTypeQueryColumn>();
                    CollectColumns(recordGraph, recordColumns);

                    columns.AddRange(recordColumns.Select(c =>
                    {
                        var l = c.AccessPathList.ToList();
                        l.Insert(0, new(FirstElement, PathType.ArrayIndex));
                        l.Insert(0, new (attributeNamePascalCase, PathType.Attribute));
                        return new CkTypeQueryColumn(
                            attributeNameCamelCase + string.Format(Array, FirstElement) + Separator + c.Path, l,
                            c.ValueType);
                    }));

                    columns.AddRange(recordColumns.Select(c =>
                    {
                        var l = c.AccessPathList.ToList();
                        l.Insert(0, new(AllElements, PathType.ArrayIndex));
                        l.Insert(0, new (attributeNamePascalCase, PathType.Attribute));
                        return new CkTypeQueryColumn(
                            attributeNameCamelCase + string.Format(Array, AllElements) + Separator + c.Path, l,
                            c.ValueType);
                    }));

                    columns.AddRange(recordColumns.Select(c =>
                    {
                        var l = c.AccessPathList.ToList();
                        l.Insert(0, new(LastElement, PathType.ArrayIndex));
                        l.Insert(0, new (attributeNamePascalCase, PathType.Attribute));
                        return new CkTypeQueryColumn(
                            attributeNameCamelCase + string.Format(Array, LastElement) + Separator + c.Path, l,
                            c.ValueType);
                    }));

                    break;
                case AttributeValueTypesDto.StringArray:
                case AttributeValueTypesDto.IntArray:

                    var l = new List<PathTerm> { new(attributeNamePascalCase, PathType.Attribute), new(FirstElement, PathType.ArrayIndex) };
                    columns.Add(new CkTypeQueryColumn(attributeNameCamelCase + string.Format(Array, FirstElement), l,
                        attributeGraph.ValueType));

                    l = [new(attributeNamePascalCase, PathType.Attribute), new(AllElements, PathType.ArrayIndex)];
                    columns.Add(new CkTypeQueryColumn(attributeNameCamelCase + string.Format(Array, AllElements), l,
                        attributeGraph.ValueType));
                    l = [new(attributeNamePascalCase, PathType.Attribute), new(LastElement, PathType.ArrayIndex)];
                    columns.Add(new CkTypeQueryColumn(attributeNameCamelCase + string.Format(Array, LastElement), l,
                        attributeGraph.ValueType));
                    break;
                default:
                    l = [new(attributeNamePascalCase, PathType.Attribute)];
                    columns.Add(new CkTypeQueryColumn(attributeNameCamelCase, l, attributeGraph.ValueType));
                    break;
            }
        }
    }
}
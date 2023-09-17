using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects.Ck;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

internal class AttributeCodeGenerator
{
    internal static void GenerateNullableProperty(CkTypeAttributeDto ckTypeAttributeDto, StringBuilder sb, CkAttributeGraph ckAttributeGraph)
    {
        switch (ckAttributeGraph.ValueType)
        {
            case AttributeValueTypesDto.String:
                sb.AppendLine($"  public string? {ckTypeAttributeDto.AttributeName}");
                sb.AppendLine("  {");
                sb.AppendLine("      get => GetAttributeStringValueOrDefault(nameof(" + ckTypeAttributeDto.AttributeName + "));");
                sb.AppendLine("      set => SetAttributeValue(nameof(" + ckTypeAttributeDto.AttributeName +
                              "), AttributeValueTypesDto.String, value);");
                sb.AppendLine("  }");
                break;
            case AttributeValueTypesDto.Int:
                sb.AppendLine($"  public long? {ckTypeAttributeDto.AttributeName}");
                sb.AppendLine("  {");
                sb.AppendLine("      get => GetAttributeValueOrDefault<long>(nameof(" + ckTypeAttributeDto.AttributeName + "));");
                sb.AppendLine("      set => SetAttributeValue(nameof(" + ckTypeAttributeDto.AttributeName +
                              "), AttributeValueTypesDto.Int, value);");
                sb.AppendLine("  }");
                break;
            case AttributeValueTypesDto.DateTime:
                sb.AppendLine($"  public global::System.DateTime? {ckTypeAttributeDto.AttributeName}");
                sb.AppendLine("  {");
                sb.AppendLine("      get => GetAttributeValueOrDefault<global::System.DateTime>(nameof(" + ckTypeAttributeDto.AttributeName +
                              "));");
                sb.AppendLine("      set => SetAttributeValue(nameof(" + ckTypeAttributeDto.AttributeName +
                              "), AttributeValueTypesDto.DateTime, value);");
                sb.AppendLine("  }");
                break;
            case AttributeValueTypesDto.Double:
                sb.AppendLine($"  public double? {ckTypeAttributeDto.AttributeName}");
                sb.AppendLine("  {");
                sb.AppendLine("      get => GetAttributeValueOrDefault<double>(nameof(" + ckTypeAttributeDto.AttributeName + "));");
                sb.AppendLine("      set => SetAttributeValue(nameof(" + ckTypeAttributeDto.AttributeName +
                              "), AttributeValueTypesDto.Double, value);");
                sb.AppendLine("  }");
                break;
            case AttributeValueTypesDto.Boolean:
                sb.AppendLine($"  public bool? {ckTypeAttributeDto.AttributeName}");
                sb.AppendLine("  {");
                sb.AppendLine("      get => GetAttributeValueOrDefault<bool>(nameof(" + ckTypeAttributeDto.AttributeName + "));");
                sb.AppendLine("      set => SetAttributeValue(nameof(" + ckTypeAttributeDto.AttributeName +
                              "), AttributeValueTypesDto.Boolean, value);");
                sb.AppendLine("  }");
                break;
            case AttributeValueTypesDto.Enum:
                if (ckAttributeGraph.ValueCkEnumId.HasValue)
                {
                    sb.AppendLine(
                        $"  public Rt{ckAttributeGraph.ValueCkEnumId.Value.Key.EnumId.MakeClassName()}Enum? {ckTypeAttributeDto.AttributeName}");
                    sb.AppendLine("  {");
                    sb.AppendLine($"      get => GetAttributeValueOrDefault<Rt{ckAttributeGraph.ValueCkEnumId.Value.Key.EnumId.MakeClassName()}Enum>(nameof(" + ckTypeAttributeDto.AttributeName + "));");
                    sb.AppendLine("      set => SetAttributeValue(nameof(" + ckTypeAttributeDto.AttributeName +
                                  "), AttributeValueTypesDto.Int, value);");
                    sb.AppendLine("  }");
                }

                break;
            case AttributeValueTypesDto.StringArray:
                sb.AppendLine($"  public IEnumerable<string>? {ckTypeAttributeDto.AttributeName}");
                sb.AppendLine("  {");
                sb.AppendLine("      get => GetAttributeValuesOrDefault<string>(nameof(" + ckTypeAttributeDto.AttributeName + "));");
                sb.AppendLine("      set => SetAttributeValueNonNullable(nameof(" + ckTypeAttributeDto.AttributeName + "), AttributeValueTypesDto.StringArray, value);");
                sb.AppendLine("  }");
                break;
            case AttributeValueTypesDto.IntArray:
                sb.AppendLine($"  public IEnumerable<long>? {ckTypeAttributeDto.AttributeName}");
                sb.AppendLine("  {");
                sb.AppendLine("      get => GetAttributeValuesOrDefault<long>(nameof(" + ckTypeAttributeDto.AttributeName + "));");
                sb.AppendLine("      set => SetAttributeValue(nameof(" + ckTypeAttributeDto.AttributeName + "), AttributeValueTypesDto.IntArray, value);");
                sb.AppendLine("  }");
                break;
            default:
                sb.AppendLine($"  // Unsupported by Generator: {ckTypeAttributeDto.AttributeName} (Type: {ckAttributeGraph.ValueType})");
                break;
        }
    }

    internal static void GenerateNonNullableProperty(CkTypeAttributeDto ckTypeAttributeDto, StringBuilder sb, CkAttributeGraph ckAttributeGraph)
    {
        switch (ckAttributeGraph.ValueType)
        {
            case AttributeValueTypesDto.String:
                sb.AppendLine($"  public string {ckTypeAttributeDto.AttributeName}");
                sb.AppendLine("  {");
                sb.AppendLine("      get => GetAttributeStringValue(nameof(" + ckTypeAttributeDto.AttributeName + "));");
                sb.AppendLine("      set => SetAttributeValueNonNullable(nameof(" + ckTypeAttributeDto.AttributeName + "), AttributeValueTypesDto.String, value);");
                sb.AppendLine("  }");
                break;
            case AttributeValueTypesDto.Int:
                sb.AppendLine($"  public long {ckTypeAttributeDto.AttributeName}");
                sb.AppendLine("  {");
                sb.AppendLine("      get => GetAttributeValue<long>(nameof(" + ckTypeAttributeDto.AttributeName + "));");
                sb.AppendLine("      set => SetAttributeValueNonNullable(nameof(" + ckTypeAttributeDto.AttributeName + "), AttributeValueTypesDto.Int, value);");
                sb.AppendLine("  }");
                break;
            case AttributeValueTypesDto.DateTime:
                sb.AppendLine($"  public global::System.DateTime {ckTypeAttributeDto.AttributeName}");
                sb.AppendLine("  {");
                sb.AppendLine("      get => GetAttributeValue<global::System.DateTime>(nameof(" + ckTypeAttributeDto.AttributeName + "));");
                sb.AppendLine("      set => SetAttributeValueNonNullable(nameof(" + ckTypeAttributeDto.AttributeName + "), AttributeValueTypesDto.DateTime, value);");
                sb.AppendLine("  }");
                break;
            case AttributeValueTypesDto.Double:
                sb.AppendLine($"  public double {ckTypeAttributeDto.AttributeName}");
                sb.AppendLine("  {");
                sb.AppendLine("      get => GetAttributeValue<double>(nameof(" + ckTypeAttributeDto.AttributeName + "));");
                sb.AppendLine("      set => SetAttributeValueNonNullable(nameof(" + ckTypeAttributeDto.AttributeName + "), AttributeValueTypesDto.Double, value);");
                sb.AppendLine("  }");
                break;
            case AttributeValueTypesDto.Boolean:
                sb.AppendLine($"  public bool {ckTypeAttributeDto.AttributeName}");
                sb.AppendLine("  {");
                sb.AppendLine("      get => GetAttributeValue<bool>(nameof(" + ckTypeAttributeDto.AttributeName + "));");
                sb.AppendLine("      set => SetAttributeValueNonNullable(nameof(" + ckTypeAttributeDto.AttributeName + "), AttributeValueTypesDto.Boolean, value);");
                sb.AppendLine("  }");
                break;
            case AttributeValueTypesDto.Enum:
                if (ckAttributeGraph.ValueCkEnumId.HasValue)
                {
                    sb.AppendLine(
                        $"  public Rt{ckAttributeGraph.ValueCkEnumId.Value.Key.EnumId.MakeClassName()}Enum {ckTypeAttributeDto.AttributeName}");
                    sb.AppendLine("  {");
                    sb.AppendLine($"      get => GetAttributeValue<Rt{ckAttributeGraph.ValueCkEnumId.Value.Key.EnumId.MakeClassName()}Enum>(nameof(" + ckTypeAttributeDto.AttributeName + "));");
                    sb.AppendLine("      set => SetAttributeValueNonNullable(nameof(" + ckTypeAttributeDto.AttributeName +
                                  "), AttributeValueTypesDto.Int, value);");
                    sb.AppendLine("  }");
                }

                break;            
            case AttributeValueTypesDto.StringArray:
                sb.AppendLine($"  public IEnumerable<string> {ckTypeAttributeDto.AttributeName}");
                sb.AppendLine("  {");
                sb.AppendLine("      get => GetAttributeValues<string>(nameof(" + ckTypeAttributeDto.AttributeName + "));");
                sb.AppendLine("      set => SetAttributeValueNonNullable(nameof(" + ckTypeAttributeDto.AttributeName + "), AttributeValueTypesDto.StringArray, value);");
                sb.AppendLine("  }");
                break;
            case AttributeValueTypesDto.IntArray:
                sb.AppendLine($"  public IEnumerable<long> {ckTypeAttributeDto.AttributeName}");
                sb.AppendLine("  {");
                sb.AppendLine("      get => GetAttributeValues<long>(nameof(" + ckTypeAttributeDto.AttributeName + "));");
                sb.AppendLine("      set => SetAttributeValueNonNullable(nameof(" + ckTypeAttributeDto.AttributeName + "), AttributeValueTypesDto.IntArray, value);");
                sb.AppendLine("  }");
                break;
            default:
                sb.AppendLine($"  // Unsupported by Generator: {ckTypeAttributeDto.AttributeName} (Type: {ckAttributeGraph.ValueType})");
                break;
        }
    }
}
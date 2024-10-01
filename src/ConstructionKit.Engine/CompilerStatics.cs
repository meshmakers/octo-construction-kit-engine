using System.Runtime.CompilerServices;
using Meshmakers.Octo.ConstructionKit.Contracts;

[assembly: InternalsVisibleTo("Meshmakers.Octo.ConstructionKit.Engine.Tests")]
[assembly: InternalsVisibleTo("Meshmakers.Octo.ConstructionKit.Compiler.SystemTests")]

namespace Meshmakers.Octo.ConstructionKit.Engine;

internal static class CompilerStatics
{
    public const string AllowedCharactersInNamesRegex = @"^[a-zA-Z0-9_.]+$";
    public const string AllowedCharactersInEnumNamesRegex = @"^[_a-zA-Z][_a-zA-Z0-9]*$";

    public const string AttributesDirectoryName = "attributes";
    public const string AssociationsDirectoryName = "associations";
    public const string TypesDirectoryName = "types";
    public const string RecordsDirectoryName = "records";
    public const string EnumsDirectoryName = "enums";
    public const string MetadataFile = "ckModel.yaml";
    public const string Sample1Entity = "sampleType1.yaml";
    public const string Sample1Record = "sampleRecord1.yaml";
    public const string Sample1Enum = "sampleEnum1.yaml";
    public const string Sample1Attribute1 = "sampleAttribute1.yaml";
    public const string Sample1Attribute2 = "sampleAttribute2.yaml";
    public const string Sample1Attribute3 = "sampleAttribute3.yaml";
    public const string Sample1Association = "sampleAssocation1.yaml";

    public static IEnumerable<CkId<CkTypeId>> WhiteListedCkTypeIds { get; } =
        [new("System/Entity")];
}
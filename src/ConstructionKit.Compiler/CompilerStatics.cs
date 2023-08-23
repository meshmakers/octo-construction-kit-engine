using System.Runtime.CompilerServices;
using Meshmakers.Octo.ConstructionKit.Contracts;

[assembly:InternalsVisibleTo("Meshmakers.Octo.ConstructionKit.Compiler.Tests")]
[assembly:InternalsVisibleTo("ConstructionKit.Compiler.SystemTests")]
[assembly:InternalsVisibleTo("Meshmakers.Octo.SystematizedData.Persistence.SystemTests")]

namespace Meshmakers.Octo.ConstructionKit.Compiler;

internal static class CompilerStatics
{
    public static IEnumerable<CkId<CkTypeId>> WhiteListedCkTypeIds { get; } =
        new CkId<CkTypeId>[] { new("System/Entity") };


    public const string AllowedCharactersInNamesRegex = @"^[a-zA-Z0-9_.]+$";
    
    public const string AttributesDirectoryName = "attributes";
    public const string AssociationsDirectoryName = "associations";
    public const string TypesDirectoryName = "types";
    public const string MetadataFile = "ckModel.yaml";
    public const string Sample1Entity = "sampleType1.yaml";
    public const string Sample1Attribute = "sampleAttribute1.yaml";
    public const string Sample1Association = "sampleAssocation1.yaml";

}
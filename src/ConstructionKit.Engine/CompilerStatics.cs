using System.Runtime.CompilerServices;
using Meshmakers.Octo.ConstructionKit.Contracts;

[assembly: InternalsVisibleTo("Meshmakers.Octo.ConstructionKit.Engine.Tests")]
[assembly: InternalsVisibleTo("Meshmakers.Octo.ConstructionKit.Compiler.SystemTests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
namespace Meshmakers.Octo.ConstructionKit.Engine;

internal static class CompilerStatics
{
    /// <summary>
    /// Regex pattern for Pascal Case validation.
    /// Matches identifiers starting with uppercase letter (A-Z), followed by alphanumeric characters and underscores.
    /// Supports optional namespace prefixes (separated by dots) and version suffixes.
    /// Examples: "Entity", "Entity-1", "Query.Filter", "Machine_OrderAssignment", "Query.Filter-1"
    /// </summary>
    public const string PascalCaseRegex = @"^[A-Z][a-zA-Z0-9_]*(?:\.[A-Z][a-zA-Z0-9_]*)*(?:-\d+)?$";

    /// <summary>
    /// Regex pattern for qualified identifiers with Pascal Case element name.
    /// Supports:
    /// - Variable prefixes like ${this}/ or ${ModelName}/
    /// - Pascal Case model names like System/
    /// - Versioned model names like System-2.0.2/
    /// Element name after "/" must be Pascal Case (may include underscores).
    /// Examples: "${this}/Entity", "System/Entity", "System-2.0.2/Entity", "${this}/Machine_OrderAssignment"
    /// </summary>
    public const string QualifiedPascalCaseRegex =
        @"^(?:[A-Z][a-zA-Z0-9_]*(?:\.[A-Z][a-zA-Z0-9_]*)*|[a-zA-Z][a-zA-Z0-9._-]*|\$\{[a-zA-Z0-9_.]+\})/[A-Z][a-zA-Z0-9_]*(?:\.[A-Z][a-zA-Z0-9_]*)*(?:-\d+)?$";

    /// <summary>
    /// Regex pattern for model ID validation.
    /// Model IDs can contain letters, digits, dots, and underscores (e.g., "System", "Basic.Energy").
    /// Note: This is different from Pascal Case as model IDs may contain dots.
    /// </summary>
    public const string AllowedCharactersInModelIdRegex = @"^[a-zA-Z0-9_.]+$";

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

    public static IEnumerable<CkIdVersionRange<CkTypeId>> WhiteListedCkTypeIds { get; } =
        [new("System-1.0.0/Entity")];
}
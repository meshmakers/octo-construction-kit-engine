using Meshmakers.Octo.ConstructionKit.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

/// <summary>
///     AB#6334 / AB#6335 (F2.1 gate findings H1/H3), message 129 <c>CkInconsistentVisibility</c>: in a ckLanguage 2
///     model a <b>public</b> element may only reference <b>public</b> elements of its own model, a public interface may
///     not declare internal methods, and a type may not redeclare a public interface's method as internal. Otherwise a
///     breaking change to the internal element is capped at Minor by the classifier (rows N1–N5) while dependents reach
///     it through the public element. One test per reference kind, plus the allowed combinations.
/// </summary>
public sealed class CkV2InconsistentVisibilityCompileTests : IDisposable
{
    private readonly CkCompileFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private const string Attributes = """
        attributes:
          - id: Name
            valueType: String
          - id: Note
            valueType: String
            visibility: {Note}
          - id: Mode
            valueType: Enum
            valueCkEnumId: ${this}/Mode
            visibility: {ModeAttribute}
          - id: Home
            valueType: Record
            valueCkRecordId: ${this}/Address
            visibility: {HomeAttribute}
        """;

    private const string Enums = """
        enums:
          - enumId: Mode
            visibility: {Mode}
            values:
              - { key: 0, name: On }
              - { key: 1, name: Off }
        """;

    private const string Records = """
        records:
          - recordId: Address
            visibility: {Address}
            derivable: Any
            attributes:
              - id: ${this}/Name
                name: Name
                isOptional: true
        """;

    private const string Roles = """
        associationRoles:
          - id: Links
            inboundName: LinkedBy
            outboundName: Links
            inboundMultiplicity: N
            outboundMultiplicity: N
            visibility: {Links}
        """;

    private async Task<OperationResult> CompileAsync(Dictionary<string, string> extraFiles,
        Dictionary<string, string>? visibility = null, int ckLanguage = 2)
    {
        await _fixture.CompileAndPublishAsync(_fixture.WriteSystemModel("2.5.0"));
        var v = new Dictionary<string, string>
        {
            ["Note"] = "Public", ["ModeAttribute"] = "Public", ["HomeAttribute"] = "Public", ["Mode"] = "Public",
            ["Address"] = "Public", ["Links"] = "Public"
        };
        foreach (var (key, value) in visibility ?? [])
        {
            v[key] = value;
        }

        string Fill(string text) => v.Aggregate(text, (t, kv) => t.Replace("{" + kv.Key + "}", kv.Value));

        var files = new Dictionary<string, string>
        {
            ["attributes/a.yaml"] = Fill(Attributes),
            ["enums/e.yaml"] = Fill(Enums),
            ["records/r.yaml"] = Fill(Records),
            ["associations/r.yaml"] = Fill(Roles)
        };
        foreach (var (path, content) in extraFiles)
        {
            files[path] = content;
        }

        var operationResult = new OperationResult();
        var source = _fixture.WriteSource($"own-{Guid.NewGuid():N}", "Own-1.0.0", ["System-[2.5,3.0)"], files,
            ckLanguage);
        try
        {
            await _fixture.Services.GetRequiredService<Contracts.Services.ICompilerService>()
                .CompileInMemoryAsync(source, operationResult);
        }
        catch (Exception)
        {
            // asserted through the messages
        }

        return operationResult;
    }

    private static Dictionary<string, string> Types(string yaml) => new() { ["types/t.yaml"] = "types:\n" + yaml };

    private static void Assert129(OperationResult result, string referrer, string referenced)
    {
        var message = Assert.Single(result.Messages, m => m.MessageNumber == 129);
        Assert.Contains(referrer, message.MessageText);
        Assert.Contains(referenced, message.MessageText);
    }

    private static void AssertNo129(OperationResult result)
    {
        Assert.DoesNotContain(result.Messages, m => m.MessageNumber == 129);
        Assert.DoesNotContain(result.Messages, m => m.MessageLevel >= Contracts.Messages.MessageLevel.Error);
    }

    private const string Helper = """
          - typeId: Helper
            derivedFromCkTypeId: ${System}/Entity
            visibility: {Helper}
            derivable: Any
            attributes:
              - id: ${this}/Note
                name: Note
                isOptional: true

        """;

    // ---- Type referrers ----

    [Fact]
    public async Task PublicType_DerivingFromAnInternalBase_Is129()
    {
        // Gate case X1 / 2k / 2l.
        var result = await CompileAsync(Types(Helper.Replace("{Helper}", "Internal") + """
              - typeId: Pump
                derivedFromCkTypeId: ${this}/Helper
                derivable: Any
            """), new() { ["Note"] = "Internal" });

        Assert129(result, "Own-1.0.0/Pump-1", "internal type 'Own-1.0.0/Helper-1' (base type)");
    }

    [Fact]
    public async Task PublicType_AssigningAnInternalAttribute_Is129()
    {
        // Gate case 2h / 7ac.
        var result = await CompileAsync(Types("""
              - typeId: Machine
                derivedFromCkTypeId: ${System}/Entity
                attributes:
                  - id: ${this}/Note
                    name: Note
                    isOptional: true
            """), new() { ["Note"] = "Internal" });

        Assert129(result, "Own-1.0.0/Machine-1", "internal attribute 'Own-1.0.0/Note-1'");
    }

    [Fact]
    public async Task PublicType_UsingAnInternalRole_Is129()
    {
        // Gate case 2i / 2j.
        var result = await CompileAsync(Types("""
              - typeId: Machine
                derivedFromCkTypeId: ${System}/Entity
                associations:
                  - id: ${this}/Links
                    targetCkTypeId: ${this}/Machine
            """), new() { ["Links"] = "Internal" });

        Assert129(result, "Own-1.0.0/Machine-1", "internal association role 'Own-1.0.0/Links-1'");
    }

    [Fact]
    public async Task PublicType_TargetingAnInternalType_Is129()
    {
        var result = await CompileAsync(Types(Helper.Replace("{Helper}", "Internal") + """
              - typeId: Machine
                derivedFromCkTypeId: ${System}/Entity
                associations:
                  - id: ${this}/Links
                    targetCkTypeId: ${this}/Helper
            """), new() { ["Note"] = "Internal" });

        Assert129(result, "Own-1.0.0/Machine-1", "internal type 'Own-1.0.0/Helper-1' (association target type)");
    }

    [Fact]
    public async Task PublicType_ImplementingOrTargetingAnInternalInterface_Is129()
    {
        // Gate case 2m (platform-owner decision 2026-10-10: interfaces are included).
        var files = Types("""
              - typeId: Machine
                derivedFromCkTypeId: ${System}/Entity
                implements:
                  - ${this}/Named-1
                attributes:
                  - id: ${this}/Name
                    name: Name
                    isOptional: true
                associations:
                  - id: ${this}/Links
                    targetCkTypeId: ${System}/Entity
                    targetCkInterfaceId: ${this}/Named-1
            """);
        files["interfaces/i.yaml"] = """
            interfaces:
              - interfaceId: Named-1
                visibility: Internal
                attributes:
                  - id: ${this}/Name
                    name: Name
                    isOptional: true
            """;

        var result = await CompileAsync(files);

        var messages = result.Messages.Where(m => m.MessageNumber == 129).Select(m => m.MessageText).ToList();
        Assert.Equal(2, messages.Count);
        Assert.Contains(messages, m => m.Contains("(implemented interface)"));
        Assert.Contains(messages, m => m.Contains("(association target interface)"));
    }

    [Fact]
    public async Task PublicMethodOfAPublicType_UsingInternalRecordsOrEnums_Is129()
    {
        // Gate cases 2d / 2f: parameter and result.
        var result = await CompileAsync(Types("""
              - typeId: Machine
                derivedFromCkTypeId: ${System}/Entity
                methods:
                  - methodId: Configure-1
                    parameters:
                      - name: mode
                        valueType: Enum
                        valueCkEnumId: ${this}/Mode
                    result:
                      valueType: Record
                      valueCkRecordId: ${this}/Address
            """), new()
        {
            ["Mode"] = "Internal", ["Address"] = "Internal", ["ModeAttribute"] = "Internal",
            ["HomeAttribute"] = "Internal"
        });

        var messages = result.Messages.Where(m => m.MessageNumber == 129).Select(m => m.MessageText).ToList();
        Assert.Equal(2, messages.Count);
        Assert.All(messages, m => Assert.Contains("Own-1.0.0/Machine-1.Configure-1", m));
        Assert.Contains(messages, m => m.Contains("enum of parameter 'mode'"));
        Assert.Contains(messages, m => m.Contains("(result record)"));
    }

    // ---- Attribute, record, role and interface referrers ----

    [Fact]
    public async Task PublicAttribute_WithAnInternalValueRecordOrEnum_Is129()
    {
        // Gate cases 2d / 2e / 2f / 2g: the attribute definitions Home and Mode stay public.
        var result = await CompileAsync([], new() { ["Mode"] = "Internal", ["Address"] = "Internal" });

        var messages = result.Messages.Where(m => m.MessageNumber == 129).Select(m => m.MessageText).ToList();
        Assert.Equal(2, messages.Count);
        Assert.Contains(messages, m => m.Contains("Own-1.0.0/Mode-1' references the internal enum") &&
                                       m.Contains("(value enum)"));
        Assert.Contains(messages, m => m.Contains("Own-1.0.0/Home-1' references the internal record") &&
                                       m.Contains("(value record)"));
    }

    [Fact]
    public async Task PublicRecord_WithAnInternalBaseOrAttribute_Is129()
    {
        var files = new Dictionary<string, string>
        {
            ["records/r.yaml"] = """
                records:
                  - recordId: Address
                    visibility: Internal
                    derivable: Any
                    attributes:
                      - id: ${this}/Name
                        name: Name
                        isOptional: true
                  - recordId: Location
                    derivedFromCkRecordId: ${this}/Address
                    attributes:
                      - id: ${this}/Note
                        name: Note
                        isOptional: true
                """
        };

        var result = await CompileAsync(files, new() { ["Note"] = "Internal", ["HomeAttribute"] = "Internal" });

        var messages = result.Messages.Where(m => m.MessageNumber == 129).Select(m => m.MessageText).ToList();
        Assert.Equal(2, messages.Count);
        Assert.All(messages, m => Assert.Contains("Own-1.0.0/Location-1", m));
        Assert.Contains(messages, m => m.Contains("(base record)"));
        Assert.Contains(messages, m => m.Contains("internal attribute 'Own-1.0.0/Note-1'"));
    }

    [Fact]
    public async Task PublicRole_WithAnInternalAttribute_Is129()
    {
        var files = new Dictionary<string, string>
        {
            ["associations/r.yaml"] = """
                associationRoles:
                  - id: Links
                    inboundName: LinkedBy
                    outboundName: Links
                    inboundMultiplicity: N
                    outboundMultiplicity: N
                    attributes:
                      - id: ${this}/Note
                        name: Note
                        isOptional: true
                """
        };

        var result = await CompileAsync(files, new() { ["Note"] = "Internal" });

        Assert129(result, "Own-1.0.0/Links-1", "internal attribute 'Own-1.0.0/Note-1'");
    }

    [Fact]
    public async Task PublicInterface_WithInternalMembersExtendsOrAssociations_Is129()
    {
        var files = Types(Helper.Replace("{Helper}", "Internal"));
        files["interfaces/i.yaml"] = """
            interfaces:
              - interfaceId: Base-1
                visibility: Internal
                attributes:
                  - id: ${this}/Name
                    name: Name
                    isOptional: true
              - interfaceId: Named-1
                extends:
                  - ${this}/Base-1
                attributes:
                  - id: ${this}/Note
                    name: Note
                    isOptional: true
                associations:
                  - id: ${this}/Links
                    targetCkTypeId: ${this}/Helper
                    isOptional: true
            """;

        var result = await CompileAsync(files, new() { ["Note"] = "Internal", ["Links"] = "Internal" });

        var messages = result.Messages.Where(m => m.MessageNumber == 129).Select(m => m.MessageText).ToList();
        Assert.All(messages, m => Assert.Contains("Own-1.0.0/Named-1", m));
        Assert.Contains(messages, m => m.Contains("(extended interface)"));
        Assert.Contains(messages, m => m.Contains("(member attribute)"));
        Assert.Contains(messages, m => m.Contains("internal association role"));
        Assert.Contains(messages, m => m.Contains("(association target type)"));
        Assert.Equal(4, messages.Count);
    }

    // ---- Interface methods (AB#6335, H3) ----

    private static Dictionary<string, string> InterfaceWithMethod(string interfaceVisibility, string methodVisibility,
        string types = "")
    {
        var files = types.Length == 0 ? new Dictionary<string, string>() : Types(types);
        files["interfaces/i.yaml"] = $$"""
            interfaces:
              - interfaceId: Named-1
                visibility: {{interfaceVisibility}}
                attributes:
                  - id: ${this}/Name
                    name: Name
                    isOptional: true
                methods:
                  - methodId: Reset-1
                    visibility: {{methodVisibility}}
            """;
        return files;
    }

    [Fact]
    public async Task PublicInterface_WithAnInternalMethod_Is129()
    {
        // Gate case 7ab / X10b.
        var result = await CompileAsync(InterfaceWithMethod("Public", "Internal"));

        Assert129(result, "Own-1.0.0/Named-1", "declares the internal method 'Reset-1'");
    }

    [Fact]
    public async Task RedeclaringAPublicInterfaceMethodAsInternal_Is129()
    {
        var result = await CompileAsync(InterfaceWithMethod("Public", "Public", """
              - typeId: Gadget
                derivedFromCkTypeId: ${System}/Entity
                implements:
                  - ${this}/Named-1
                methods:
                  - methodId: Reset-1
                    visibility: Internal
            """));

        Assert129(result, "Own-1.0.0/Gadget-1", "redeclares method 'Reset-1' of the public interface");
    }

    [Fact]
    public async Task InternalInterface_WithAnInternalMethod_AndPublicTypeWithAnInternalMethod_AreAllowed()
    {
        var result = await CompileAsync(InterfaceWithMethod("Internal", "Internal", """
              - typeId: Gadget
                derivedFromCkTypeId: ${System}/Entity
                visibility: Internal
                implements:
                  - ${this}/Named-1
              - typeId: Machine
                derivedFromCkTypeId: ${System}/Entity
                methods:
                  - methodId: Calibrate-1
                    visibility: Internal
                    parameters:
                      - name: mode
                        valueType: Enum
                        valueCkEnumId: ${this}/Mode
            """), new() { ["Mode"] = "Internal", ["ModeAttribute"] = "Internal" });

        AssertNo129(result);
    }

    // ---- Allowed combinations ----

    [Fact]
    public async Task InternalToPublic_InternalToInternal_PublicToPublic_AreAllowed()
    {
        var result = await CompileAsync(Types(Helper.Replace("{Helper}", "Internal") + """
              - typeId: Inner
                derivedFromCkTypeId: ${this}/Helper
                visibility: Internal
                attributes:
                  - id: ${this}/Home
                    name: Home
                    isOptional: true
              - typeId: Machine
                derivedFromCkTypeId: ${System}/Entity
                attributes:
                  - id: ${this}/Mode
                    name: Mode
                    isOptional: true
                  - id: ${this}/Home
                    name: Home
                    isOptional: true
            """), new() { ["Note"] = "Internal" });

        AssertNo129(result);
    }

    [Fact]
    public async Task V1Model_IsNotChecked()
    {
        // ckLanguage 1 has no visibility at all: every declaration is reported as 90, never as 129.
        var result = await CompileAsync(Types("""
              - typeId: Machine
                derivedFromCkTypeId: ${System}/Entity
                attributes:
                  - id: ${this}/Note
                    name: Note
                    isOptional: true
            """), new() { ["Note"] = "Internal" }, ckLanguage: 1);

        Assert.DoesNotContain(result.Messages, m => m.MessageNumber == 129);
        Assert.Contains(result.Messages, m => m.MessageNumber == 90);
    }
}

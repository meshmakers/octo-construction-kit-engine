using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.CkV2;

/// <summary>
///     CK v2 Phase 0 (AB#5667 / AB#5668 / AB#5669): the wire side of <c>ckLanguage</c>, <c>interfaces</c>,
///     <c>implements</c>, attribute <c>access</c> and <c>methods</c>. The element schemas keep
///     <c>additionalProperties: false</c>, so every key must be listed or it is a hard parse failure.
/// </summary>
public class CkV2SchemaTests
{
    private static readonly UTF8Encoding NoBomUtf8 = new(encoderShouldEmitUTF8Identifier: false);

    private const string ElementsHeader =
        "\"$schema\": \"https://schemas.meshmakers.cloud/construction-kit-elements.schema.json\"\n";

    private const string KitchenSinkElementsYaml = ElementsHeader + """
        interfaces:
          - interfaceId: Named-1
            description: Anything with a name
            attributes:
              - id: ${System}/Name
                name: Name
              - id: ${System}/Description
                name: Description
                isOptional: true
        types:
          - typeId: User
            derivedFromCkTypeId: ${System}/Entity
            implements:
              - ${this}/Named-1
            attributes:
              - id: ${System}/Name
                name: Name
              - id: ${this}/PasswordHash
                name: PasswordHash
                isOptional: true
                access: Hidden
              - id: ${this}/Login
                name: Login
                access: ReadOnly
              - id: ${this}/Status
                name: Status
                access: MethodOnly
              - id: ${this}/Comment
                name: Comment
                access: ReadWrite
            methods:
              - methodId: ChangePassword-1
                kind: Instance
                description: Changes the password
                parameters:
                  - { name: currentPassword, valueType: String, isOptional: true, sensitive: true }
                  - { name: newPassword, valueType: String, sensitive: true, description: The new password }
                  - name: mode
                    valueType: Enum
                    valueCkEnumId: ${this}/Mode
                  - name: address
                    valueType: Record
                    valueCkRecordId: ${this}/Address
                result:
                  valueType: Record
                  valueCkRecordId: ${this}/Address
                errors:
                  - code: PASSWORD_POLICY_VIOLATION
                    description: The password violates the policy
                  - code: USER_LOCKED_OUT
                authorization:
                  roles: [ UserManagement ]
                  allowSelf: true
                  scopes: [ extra ]
                execution:
                  timeoutSeconds: 15
                  idempotent: false
              - methodId: Unlock-1
              - methodId: CreateMany-2
                kind: Static
        records:
          - recordId: Address
            attributes:
              - id: ${this}/Street
                name: Street
                access: Hidden
        associationRoles:
          - id: Owns
            inboundName: OwnedBy
            outboundName: Owns
            inboundMultiplicity: N
            outboundMultiplicity: N
            attributes:
              - id: ${this}/Since
                name: Since
                access: ReadOnly
        """;

    private static Stream ToStream(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static bool ValidateElements(string yaml, out OperationResult operationResult)
    {
        operationResult = new OperationResult();
        return new CkSchemaValidator().ValidateElementsInYaml(ToStream(yaml), "inline.yaml", operationResult);
    }

    [Fact]
    public async Task Elements_KitchenSink_ParsesEveryNewKey()
    {
        var serializer = new CkYamlSerializer(new CkSchemaValidator());
        var operationResult = new OperationResult();
        var elements = await serializer.DeserializeElementsAsync(ToStream(KitchenSinkElementsYaml), "inline.yaml",
            operationResult);
        Assert.False(operationResult.HasErrors, string.Join("; ", operationResult.Messages));

        var ckInterface = Assert.Single(elements.Interfaces!);
        Assert.Equal(new CkInterfaceId("Named", 1), ckInterface.InterfaceId);
        Assert.Equal("Anything with a name", ckInterface.Description);
        Assert.Equal(2, ckInterface.Attributes.Count);
        Assert.Equal("${System}", ckInterface.Attributes[0].CkAttributeId.ModelId.Name);
        Assert.Equal("Name", ckInterface.Attributes[0].CkAttributeId.ElementId.Name);
        Assert.Equal("Name", ckInterface.Attributes[0].AttributeName);
        Assert.False(ckInterface.Attributes[0].IsOptional);
        Assert.True(ckInterface.Attributes[1].IsOptional);

        var user = Assert.Single(elements.Types!);
        Assert.Equal("Named-1", Assert.Single(user.Implements!).ElementId.FullName);

        var access = user.Attributes!.ToDictionary(a => a.AttributeName, a => a.Access);
        Assert.Null(access["Name"]);
        Assert.Equal(CkAttributeAccessDto.Hidden, access["PasswordHash"]);
        Assert.Equal(CkAttributeAccessDto.ReadOnly, access["Login"]);
        Assert.Equal(CkAttributeAccessDto.MethodOnly, access["Status"]);
        Assert.Equal(CkAttributeAccessDto.ReadWrite, access["Comment"]);
        Assert.Equal(CkAttributeAccessDto.Hidden, elements.Records!.Single().Attributes!.Single().Access);
        Assert.Equal(CkAttributeAccessDto.ReadOnly, elements.AssociationRoles!.Single().Attributes!.Single().Access);

        Assert.Equal(3, user.Methods!.Count);
        var changePassword = user.Methods[0];
        Assert.Equal("ChangePassword-1", changePassword.MethodId);
        Assert.Equal(CkMethodKindDto.Instance, changePassword.Kind);
        Assert.Equal(4, changePassword.Parameters!.Count);
        Assert.True(changePassword.Parameters[0].Sensitive);
        Assert.True(changePassword.Parameters[0].IsOptional);
        Assert.Equal("The new password", changePassword.Parameters[1].Description);
        Assert.Equal(AttributeValueTypesDto.Enum, changePassword.Parameters[2].ValueType);
        Assert.NotNull(changePassword.Parameters[2].ValueCkEnumId);
        Assert.NotNull(changePassword.Parameters[3].ValueCkRecordId);
        Assert.Equal(AttributeValueTypesDto.Record, changePassword.Result!.ValueType);
        Assert.Equal(["PASSWORD_POLICY_VIOLATION", "USER_LOCKED_OUT"], changePassword.Errors!.Select(e => e.Code));
        Assert.Equal(["UserManagement"], changePassword.Authorization!.Roles!);
        Assert.True(changePassword.Authorization.AllowSelf);
        Assert.Equal(["extra"], changePassword.Authorization.Scopes!);
        Assert.Equal(15, changePassword.Execution!.TimeoutSeconds);
        Assert.False(changePassword.Execution.Idempotent);

        var minimal = user.Methods[1];
        Assert.Equal(CkMethodKindDto.Instance, minimal.Kind);
        Assert.Null(minimal.Parameters);
        Assert.Null(minimal.Result);
        Assert.Null(minimal.Errors);
        Assert.Null(minimal.Authorization);
        Assert.Null(minimal.Execution);
        Assert.Equal(CkMethodKindDto.Static, user.Methods[2].Kind);
    }

    [Theory]
    [InlineData("types:\n  - typeId: A\n    attributes:\n      - id: ${this}/X\n        name: X\n        access: Secret\n", "unknown access value")]
    [InlineData("types:\n  - typeId: A\n    implements: [ Named-1 ]\n", "implements without model part")]
    [InlineData("types:\n  - typeId: A\n    implements:\n      - ${this}/Named-1\n      - ${this}/Named-1\n", "duplicate implements")]
    [InlineData("types:\n  - typeId: A\n    methods:\n      - methodId: Do\n", "method id without version")]
    [InlineData("types:\n  - typeId: A\n    methods:\n      - methodId: do-1\n", "method id not PascalCase")]
    [InlineData("types:\n  - typeId: A\n    methods:\n      - methodId: Do-1\n        kind: Async\n", "unknown method kind")]
    [InlineData("types:\n  - typeId: A\n    methods:\n      - methodId: Do-1\n        mode: Sync\n", "unknown method key")]
    [InlineData("types:\n  - typeId: A\n    methods:\n      - methodId: Do-1\n        parameters:\n          - { name: Value, valueType: String }\n", "parameter name not camelCase")]
    [InlineData("types:\n  - typeId: A\n    methods:\n      - methodId: Do-1\n        parameters:\n          - { name: value, valueType: Binary }\n", "unsupported parameter value type")]
    [InlineData("types:\n  - typeId: A\n    methods:\n      - methodId: Do-1\n        parameters:\n          - { name: value }\n", "parameter without value type")]
    [InlineData("types:\n  - typeId: A\n    methods:\n      - methodId: Do-1\n        result: { valueType: RecordArray }\n", "unsupported result value type")]
    [InlineData("types:\n  - typeId: A\n    methods:\n      - methodId: Do-1\n        errors:\n          - code: lower_case\n", "error code not UPPER_SNAKE_CASE")]
    [InlineData("types:\n  - typeId: A\n    methods:\n      - methodId: Do-1\n        authorization: { anyone: true }\n", "unknown authorization key")]
    [InlineData("types:\n  - typeId: A\n    methods:\n      - methodId: Do-1\n        execution: { timeoutSeconds: 0 }\n", "timeout below minimum")]
    [InlineData("types:\n  - typeId: A\n    methods:\n      - methodId: Do-1\n        execution: { timeoutSeconds: 301 }\n", "timeout above maximum")]
    [InlineData("interfaces:\n  - interfaceId: Named\n    attributes:\n      - id: ${System}/Name\n        name: Name\n", "interface id without version")]
    [InlineData("interfaces:\n  - interfaceId: Named-1\n    attributes: []\n", "interface without members")]
    [InlineData("interfaces:\n  - interfaceId: Named-1\n    extends:\n      - ${this}/Base-1\n    attributes:\n      - id: ${System}/Name\n        name: Name\n", "interface extends is out of Phase 0")]
    [InlineData("interfaces:\n  - interfaceId: Named-1\n    attributes:\n      - id: ${System}/Name\n        name: Name\n        access: Hidden\n", "unknown interface member key")]
    [InlineData("interfaces:\n  - interfaceId: Named-1\n    attributes:\n      - id: ${System}/Name\n", "interface member without name")]
    public void Elements_InvalidCkV2Keys_FailSchemaValidation(string body, string reason)
    {
        var isValid = ValidateElements(ElementsHeader + body, out var operationResult);

        Assert.False(isValid, reason);
        Assert.True(operationResult.HasErrors, reason);
    }

    [Fact]
    public void Elements_KitchenSink_PassesSchemaValidation()
    {
        var isValid = ValidateElements(KitchenSinkElementsYaml, out var operationResult);

        Assert.True(isValid, string.Join("; ", operationResult.Messages));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Meta_CkLanguage_Parses(int ckLanguage)
    {
        var yaml = $"""
                    "$schema": "https://schemas.meshmakers.cloud/construction-kit-meta.schema.json"
                    modelId: Spike-1.0.0
                    ckLanguage: {ckLanguage}
                    dependencies:
                      - System-[2.5,3.0)
                    """;
        var operationResult = new OperationResult();
        var meta = await new CkYamlSerializer(new CkSchemaValidator()).DeserializeMetaAsync(ToStream(yaml), "ckModel.yaml",
            operationResult);

        Assert.False(operationResult.HasErrors);
        Assert.Equal(ckLanguage, meta.CkLanguage);
        Assert.Equal(ckLanguage, meta.EffectiveCkLanguage);
    }

    [Theory]
    [InlineData("3")]
    [InlineData("0")]
    [InlineData("two")]
    public void Meta_CkLanguage_OutOfRange_FailsSchemaValidation(string ckLanguage)
    {
        var yaml = $"""
                    "$schema": "https://schemas.meshmakers.cloud/construction-kit-meta.schema.json"
                    modelId: Spike-1.0.0
                    ckLanguage: {ckLanguage}
                    """;
        var operationResult = new OperationResult();

        Assert.False(new CkSchemaValidator().ValidateMetaInYaml(ToStream(yaml), "ckModel.yaml", operationResult));
    }

    [Fact]
    public async Task Meta_WithoutCkLanguage_IsLanguage1()
    {
        var yaml = """
                   "$schema": "https://schemas.meshmakers.cloud/construction-kit-meta.schema.json"
                   modelId: Legacy-1.0.0
                   """;
        var meta = await new CkYamlSerializer(new CkSchemaValidator()).DeserializeMetaAsync(ToStream(yaml), "ckModel.yaml",
            new OperationResult());

        Assert.Null(meta.CkLanguage);
        Assert.Equal(1, meta.EffectiveCkLanguage);
    }

    [Fact]
    public async Task CompiledModel_Yaml_RoundTripsEveryCkV2Member()
    {
        var model = CkV2TestModels.CreateModel();
        var serializer = new CkYamlSerializer(new CkSchemaValidator());

        var yaml = await SerializeYamlAsync(model);
        var operationResult = new OperationResult();
        Assert.True(new CkSchemaValidator().ValidateCompiledModelInYaml(ToStream(yaml), "inline.yaml", operationResult),
            string.Join("; ", operationResult.Messages) + "\n" + yaml);
        operationResult = new OperationResult();
        var roundTripped = serializer.DeserializeCompiledModelRoot(yaml, "inline.yaml", operationResult);

        Assert.False(operationResult.HasErrors, string.Join("; ", operationResult.Messages));
        AssertCkV2MembersEqual(model, roundTripped);
    }

    [Fact]
    public async Task CompiledModel_Json_RoundTripsEveryCkV2MemberThroughSchemaValidation()
    {
        var model = CkV2TestModels.CreateModel();
        var serializer = new CkJsonSerializer();

        var json = await SerializeJsonAsync(model);
        Assert.Contains("\"ckLanguage\": 2", json);
        Assert.Contains("\"access\": \"Hidden\"", json);
        Assert.Contains("\"implements\": [", json);
        Assert.Contains("\"TestModel/Serialized-1\"", json.Replace("TestModel-1.0.0", "TestModel"));

        var operationResult = new OperationResult();
        var roundTripped = serializer.DeserializeCompiledModelRoot(json, "inline.json", operationResult);

        Assert.False(operationResult.HasErrors, string.Join("; ", operationResult.Messages));
        AssertCkV2MembersEqual(model, roundTripped);
    }

    [Fact]
    public async Task CompiledModel_HigherCkLanguage_IsNotASchemaError()
    {
        // A future engine may write ckLanguage 3. The compiled schema accepts it so the reader can report the
        // dedicated message 91 (CkLanguageNotSupported) instead of an opaque schema violation.
        var model = SemVerTestModels.CreateModel();
        model.CkLanguage = 3;
        var json = await SerializeJsonAsync(model);

        var operationResult = new OperationResult();
        var roundTripped = new CkJsonSerializer().DeserializeCompiledModelRoot(json, "inline.json", operationResult);

        Assert.False(operationResult.HasErrors);
        Assert.Equal(3, roundTripped.CkLanguage);
    }

    [Fact]
    public async Task CompiledModel_WithoutCkV2Members_EmitsNoCkV2Keys()
    {
        // Back-compat guard: a ckLanguage 1 model serializes byte-identical to before CK v2.
        var model = SemVerTestModels.CreateModel();

        var yaml = await SerializeYamlAsync(model);
        var json = await SerializeJsonAsync(model);

        foreach (var key in new[] { "ckLanguage", "interfaces", "implements", "methods", "access" })
        {
            Assert.DoesNotContain(key, yaml);
            Assert.DoesNotContain($"\"{key}\"", json);
        }
    }

    private static void AssertCkV2MembersEqual(CkCompiledModelRoot expected, CkCompiledModelRoot actual)
    {
        Assert.Equal(expected.CkLanguage, actual.CkLanguage);

        var expectedInterface = Assert.Single(expected.Interfaces!);
        var actualInterface = Assert.Single(actual.Interfaces!);
        Assert.Equal(expectedInterface.InterfaceId, actualInterface.InterfaceId);
        Assert.Equal(expectedInterface.Description, actualInterface.Description);
        Assert.Equal(expectedInterface.Attributes.Select(a => (a.CkAttributeId, a.AttributeName, a.IsOptional)),
            actualInterface.Attributes.Select(a => (a.CkAttributeId, a.AttributeName, a.IsOptional)));

        var expectedType = SemVerTestModels.GetMachine(expected);
        var actualType = SemVerTestModels.GetMachine(actual);
        Assert.Equal(expectedType.Implements!, actualType.Implements!);
        Assert.Equal(expectedType.Attributes!.Select(a => a.Access), actualType.Attributes!.Select(a => a.Access));
        Assert.Equal(SemVerTestModels.GetRecord(expected).Attributes!.Select(a => a.Access),
            SemVerTestModels.GetRecord(actual).Attributes!.Select(a => a.Access));

        Assert.Equal(expectedType.Methods!.Count, actualType.Methods!.Count);
        for (var i = 0; i < expectedType.Methods.Count; i++)
        {
            CkV2Assert.MethodsEqual(expectedType.Methods[i], actualType.Methods[i]);
        }
    }

    private static async Task<string> SerializeYamlAsync(CkCompiledModelRoot model)
    {
        var serializer = new CkYamlSerializer(new CkSchemaValidator());
        using var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, NoBomUtf8, leaveOpen: true))
        {
            await serializer.SerializeAsync(writer, model);
            await writer.FlushAsync(TestContext.Current.CancellationToken);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async Task<string> SerializeJsonAsync(CkCompiledModelRoot model)
    {
        var serializer = new CkJsonSerializer();
        using var stream = new MemoryStream();
        await using (var writer = new StreamWriter(stream, NoBomUtf8, leaveOpen: true))
        {
            await serializer.SerializeAsync(writer, model);
            await writer.FlushAsync(TestContext.Current.CancellationToken);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}

/// <summary>
///     Field-by-field comparison of CK v2 method definitions.
/// </summary>
internal static class CkV2Assert
{
    public static void MethodsEqual(CkMethodDto expected, CkMethodDto actual)
    {
        Assert.Equal(expected.MethodId, actual.MethodId);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Description, actual.Description);
        Assert.Equal(expected.Parameters?.Select(p => (p.Name, p.ValueType, p.ValueCkRecordId, p.ValueCkEnumId,
                p.IsOptional, p.Sensitive, p.Description)),
            actual.Parameters?.Select(p => (p.Name, p.ValueType, p.ValueCkRecordId, p.ValueCkEnumId, p.IsOptional,
                p.Sensitive, p.Description)));
        Assert.Equal(expected.Result?.ValueType, actual.Result?.ValueType);
        Assert.Equal(expected.Result?.ValueCkRecordId, actual.Result?.ValueCkRecordId);
        Assert.Equal(expected.Result?.ValueCkEnumId, actual.Result?.ValueCkEnumId);
        Assert.Equal(expected.Errors?.Select(e => (e.Code, e.Description)),
            actual.Errors?.Select(e => (e.Code, e.Description)));
        Assert.Equal(expected.Authorization?.Roles, actual.Authorization?.Roles);
        Assert.Equal(expected.Authorization?.AllowSelf, actual.Authorization?.AllowSelf);
        Assert.Equal(expected.Authorization?.Scopes, actual.Authorization?.Scopes);
        Assert.Equal(expected.Execution?.TimeoutSeconds, actual.Execution?.TimeoutSeconds);
        Assert.Equal(expected.Execution?.Idempotent, actual.Execution?.Idempotent);
    }
}

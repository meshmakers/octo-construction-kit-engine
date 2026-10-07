using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Resolvers;

/// <summary>
///     CK v2 Phase 0 (AB#5669): method inheritance and the method rules M-1..M-5 (100-104).
/// </summary>
public class CkV2MethodResolverTests(ITestOutputHelper output) : CkV2ResolverTestBase(output)
{
    [Fact]
    public void Methods_AreInheritedWithTheirDeclaringType()
    {
        var operationResult = new OperationResult();
        var graph = Resolve(Model(), operationResult);
        Assert.Empty(operationResult.Messages);

        var principal = graph.Types[$"{M}/Principal"];
        var account = graph.Types[$"{M}/Account"];
        Assert.Equal(["ChangePassword-1", "Unlock-1"], principal.AllMethods.Keys.OrderBy(k => k));
        Assert.Equal(["ChangePassword-1", "Lock-1", "Unlock-1"], account.AllMethods.Keys.OrderBy(k => k));
        Assert.Equal(["Lock-1"], account.DefinedMethods.Select(m => m.MethodId));

        var inherited = account.AllMethods["ChangePassword-1"];
        Assert.Equal(principal.CkTypeId, inherited.DeclaringCkTypeId);
        Assert.Equal($"{M}/Principal.ChangePassword-1", inherited.QualifiedMethodId);
        Assert.Equal(15, inherited.TimeoutSeconds);
        Assert.Equal(CkMethodExecutionDto.DefaultTimeoutSeconds, account.AllMethods["Unlock-1"].TimeoutSeconds);
        Assert.Equal($"{M}/Account.Lock-1", account.AllMethods["Lock-1"].QualifiedMethodId);
        Assert.Empty(graph.Types[$"{M}/Tag"].AllMethods);
    }

    [Fact]
    public void Code100_OverridingAnInheritedMethod()
    {
        var model = Model();
        Type(model, "Account").Methods!.Add(new CkMethodDto { MethodId = "Unlock-1" });

        var message = Assert.Single(ResolveExpectingOnly(model, 100));
        Assert.Contains("inherited", message.MessageText);
    }

    [Fact]
    public void Code100_DuplicateMethodIdOnType()
    {
        var model = Model();
        Type(model, "Tag").Methods = [new() { MethodId = "Ping-1" }, new() { MethodId = "Ping-1" }];

        Assert.Single(ResolveExpectingOnly(model, 100));
    }

    [Fact]
    public void Code100_NewVersionOfAnInheritedMethodName_OK()
    {
        var model = Model();
        Type(model, "Account").Methods!.Add(new CkMethodDto { MethodId = "Unlock-2" });

        ResolveExpectingNoMessages(model);
    }

    [Theory]
    [InlineData("Create-1")]
    [InlineData("Update-2")]
    [InlineData("Delete-1")]
    public void Code102_ReservedMethodName(string methodId)
    {
        var model = Model();
        Type(model, "Tag").Methods = [new() { MethodId = methodId }];

        var message = Assert.Single(ResolveExpectingOnly(model, 102));
        Assert.Contains(methodId, message.MessageText);
    }

    [Fact]
    public void Code102_NameMerelyStartingWithReservedWord_OK()
    {
        var model = Model();
        Type(model, "Tag").Methods = [new() { MethodId = "CreateReport-1" }, new() { MethodId = "Deleted-1" }];

        ResolveExpectingNoMessages(model);
    }

    [Fact]
    public void Code101_DuplicateParameterName()
    {
        var model = Model();
        Type(model, "Tag").Methods =
        [
            new()
            {
                MethodId = "Rename-1",
                Parameters =
                [
                    new() { Name = "name", ValueType = AttributeValueTypesDto.String },
                    new() { Name = "name", ValueType = AttributeValueTypesDto.Int }
                ]
            }
        ];

        var message = Assert.Single(ResolveExpectingOnly(model, 101));
        Assert.Contains("'name'", message.MessageText);
    }

    [Fact]
    public void Code101_RecordParameterWithoutRecordId()
    {
        var model = Model();
        Type(model, "Tag").Methods =
            [new() { MethodId = "Move-1", Parameters = [new() { Name = "to", ValueType = AttributeValueTypesDto.Record }] }];

        var message = Assert.Single(ResolveExpectingOnly(model, 101));
        Assert.Contains("valueCkRecordId", message.MessageText);
    }

    [Fact]
    public void Code101_EnumResultWithoutEnumId()
    {
        var model = Model();
        Type(model, "Tag").Methods =
            [new() { MethodId = "Probe-1", Result = new() { ValueType = AttributeValueTypesDto.Enum } }];

        var message = Assert.Single(ResolveExpectingOnly(model, 101));
        Assert.Contains("valueCkEnumId", message.MessageText);
    }

    [Fact]
    public void Code101_RecordIdOnNonRecordParameter()
    {
        var model = Model();
        Type(model, "Tag").Methods =
        [
            new()
            {
                MethodId = "Move-1",
                Parameters = [new() { Name = "to", ValueType = AttributeValueTypesDto.String, ValueCkRecordId = $"{M}/Address" }]
            }
        ];

        Assert.Single(ResolveExpectingOnly(model, 101));
    }

    [Fact]
    public void Code101_UnknownRecordAndEnumReferences()
    {
        var model = Model();
        Type(model, "Tag").Methods =
        [
            new()
            {
                MethodId = "Move-1",
                Parameters = [new() { Name = "to", ValueType = AttributeValueTypesDto.Record, ValueCkRecordId = $"{M}/Nowhere" }],
                Result = new() { ValueType = AttributeValueTypesDto.Enum, ValueCkEnumId = $"{M}/NoSuchEnum" }
            }
        ];

        var messages = ResolveExpectingOnly(model, 101);
        Assert.Contains(messages, m => m.MessageText.Contains("Nowhere"));
        Assert.Contains(messages, m => m.MessageText.Contains("NoSuchEnum"));
    }

    [Fact]
    public void Code103_ReservedErrorCodePrefix()
    {
        var model = Model();
        Type(model, "Tag").Methods = [new() { MethodId = "Ping-1", Errors = [new() { Code = "METHOD_TIMEOUT" }] }];

        var message = Assert.Single(ResolveExpectingOnly(model, 103));
        Assert.Contains("METHOD_TIMEOUT", message.MessageText);
    }

    [Fact]
    public void Code103_DuplicateErrorCode()
    {
        var model = Model();
        Type(model, "Tag").Methods =
            [new() { MethodId = "Ping-1", Errors = [new() { Code = "BUSY" }, new() { Code = "BUSY" }] }];

        Assert.Single(ResolveExpectingOnly(model, 103));
    }

    [Fact]
    public void Code104_AllowSelfOnStaticMethod()
    {
        var model = Model();
        Type(model, "Tag").Methods =
        [
            new() { MethodId = "Ping-1", Kind = CkMethodKindDto.Static, Authorization = new() { AllowSelf = true } }
        ];

        var message = Assert.Single(ResolveExpectingOnly(model, 104));
        Assert.Contains("allowSelf", message.MessageText);
    }

    [Fact]
    public void Code104_AllowSelfOnInstanceMethod_OK()
    {
        // The kitchen sink's ChangePassword-1 is an instance method with allowSelf: true.
        Assert.True(Type(Model(), "Principal").Methods![0].Authorization!.AllowSelf);
        ResolveExpectingNoMessages(Model());
    }
}

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

    // ── execution.handler (AB#6877, message 131) ─────────────────────────────────────

    [Fact]
    public void Code131_PipelineMethodWithExplicitTimeout_OK_AndReachesTheGraph()
    {
        var model = Model();
        Type(model, "Tag").Methods =
        [
            new()
            {
                MethodId = "Ping-1", Execution = new() { TimeoutSeconds = 120, Handler = CkMethodHandlers.Pipeline }
            }
        ];

        var operationResult = new OperationResult();
        var graph = Resolve(model, operationResult);

        Assert.Empty(operationResult.Messages);
        var method = graph.Types[$"{M}/Tag"].AllMethods["Ping-1"];
        Assert.Equal(CkMethodHandlers.Pipeline, method.Handler);
        Assert.Equal(120, method.TimeoutSeconds);
        Assert.Equal($"{M}/Tag.Ping-1", method.QualifiedMethodId);
    }

    [Fact]
    public void Code131_PipelineMethodWithoutTimeout()
    {
        var model = Model();
        Type(model, "Tag").Methods =
        [
            new() { MethodId = "Ping-1", Execution = new() { Handler = CkMethodHandlers.Pipeline, Idempotent = true } }
        ];

        var message = Assert.Single(ResolveExpectingOnly(model, 131));
        Assert.Contains("Ping-1", message.MessageText);
        Assert.Contains("timeoutSeconds", message.MessageText);
    }

    [Theory]
    [InlineData("Pipeline")]
    [InlineData("PIPELINE")]
    [InlineData("script")]
    [InlineData("")]
    public void Code131_UnknownHandlerValue(string handler)
    {
        var model = Model();
        Type(model, "Tag").Methods =
        [
            new() { MethodId = "Ping-1", Execution = new() { TimeoutSeconds = 10, Handler = handler } }
        ];

        var message = Assert.Single(ResolveExpectingOnly(model, 131));
        Assert.Contains("Ping-1", message.MessageText);
        Assert.Contains("unknown handler", message.MessageText);
    }

    [Fact]
    public void Code131_PipelineOnAnInterfaceMethod()
    {
        var model = Model();
        (model.Interfaces ??= []).Add(new CkInterfaceDto
        {
            InterfaceId = "Labeled-1",
            Methods =
            [
                new() { MethodId = "Relabel-1", Execution = new() { TimeoutSeconds = 10, Handler = CkMethodHandlers.Pipeline } }
            ]
        });

        var message = Assert.Single(ResolveExpectingOnly(model, 131));
        Assert.Contains("Relabel-1", message.MessageText);
        Assert.Contains("interface method", message.MessageText);
    }

    [Fact]
    public void Code131_ExplicitServiceHandler_OK_AndImplementingTypeMayRedeclareAsPipeline()
    {
        var model = Model();
        (model.Interfaces ??= []).Add(new CkInterfaceDto
        {
            InterfaceId = "Labeled-1",
            Methods = [new() { MethodId = "Relabel-1", Execution = new() { Handler = CkMethodHandlers.Service } }]
        });
        var tag = Type(model, "Tag");
        tag.Implements = [$"{M}/Labeled-1"];
        // Execution is not part of the invocation contract (rule 122): the implementing type may bind the method to a
        // pipeline itself.
        tag.Methods =
        [
            new() { MethodId = "Relabel-1", Execution = new() { TimeoutSeconds = 30, Handler = CkMethodHandlers.Pipeline } }
        ];

        ResolveExpectingNoMessages(model);
    }

    // ── sensitive parameter + pipeline handler (AB#7004, OD-9 Option A, message 132) ──

    private static CkMethodDto SensitiveMethod(string? handler, int? timeoutSeconds = 30) => new()
    {
        MethodId = "SetSecret-1",
        Parameters =
        [
            new() { Name = "name", ValueType = AttributeValueTypesDto.String },
            new() { Name = "secret", ValueType = AttributeValueTypesDto.String, Sensitive = true }
        ],
        Execution = handler == null && timeoutSeconds == null
            ? null
            : new() { Handler = handler, TimeoutSeconds = timeoutSeconds }
    };

    [Fact]
    public void Code132_SensitiveParameterWithPipelineHandler_NamesMethodTypeAndParameter()
    {
        var model = Model();
        Type(model, "Tag").Methods = [SensitiveMethod(CkMethodHandlers.Pipeline)];

        var message = Assert.Single(ResolveExpectingOnly(model, 132));
        Assert.Contains("SetSecret-1", message.MessageText);
        Assert.Contains("/Tag", message.MessageText);
        Assert.Contains("'secret'", message.MessageText);
        Assert.DoesNotContain("'name'", message.MessageText);
        Assert.Contains("execution.handler: service", message.MessageText);
    }

    [Fact]
    public void Code132_SeveralSensitiveParameters_AreAllNamed()
    {
        var model = Model();
        var method = SensitiveMethod(CkMethodHandlers.Pipeline);
        method.Parameters!.Add(new() { Name = "token", ValueType = AttributeValueTypesDto.String, Sensitive = true });
        Type(model, "Tag").Methods = [method];

        var message = Assert.Single(ResolveExpectingOnly(model, 132));
        Assert.Contains("'secret', 'token'", message.MessageText);
    }

    [Fact]
    public void Code132_AndCode131_AreBothReportedForAPipelineWithoutTimeout()
    {
        var model = Model();
        Type(model, "Tag").Methods = [SensitiveMethod(CkMethodHandlers.Pipeline, timeoutSeconds: null)];

        var operationResult = new OperationResult();
        Resolve(model, operationResult);

        Assert.Equal([131, 132], operationResult.Messages.Select(m => m.MessageNumber).OrderBy(n => n));
    }

    [Theory]
    [InlineData(CkMethodHandlers.Service)]
    [InlineData(null)]
    public void Code132_SensitiveParameterWithServiceOrOmittedHandler_OK(string? handler)
    {
        var model = Model();
        Type(model, "Tag").Methods = [SensitiveMethod(handler, timeoutSeconds: handler == null ? null : 30)];

        ResolveExpectingNoMessages(model);
    }

    [Fact]
    public void Code132_PipelineWithoutSensitiveParameter_OK()
    {
        var model = Model();
        var method = SensitiveMethod(CkMethodHandlers.Pipeline);
        method.Parameters!.Single(p => p.Sensitive).Sensitive = false;
        Type(model, "Tag").Methods = [method];

        ResolveExpectingNoMessages(model);
    }

    [Fact]
    public void Code132_PipelineWithANonSensitiveResult_OK()
    {
        // A pipeline method whose result is not marked sensitive is unchanged (a sensitive result is message 134).
        var model = Model();
        Type(model, "Tag").Methods =
        [
            new()
            {
                MethodId = "IssueToken-1",
                Parameters = [new() { Name = "scope", ValueType = AttributeValueTypesDto.String }],
                Result = new() { ValueType = AttributeValueTypesDto.String },
                Execution = new() { Handler = CkMethodHandlers.Pipeline, TimeoutSeconds = 30 }
            }
        ];

        ResolveExpectingNoMessages(model);
    }

    [Fact]
    public void Code132_InterfaceMethod_SensitivePipelineIsRefusedByBoth131And132()
    {
        var model = Model();
        (model.Interfaces ??= []).Add(new CkInterfaceDto
        {
            InterfaceId = "Secured-1",
            Methods = [SensitiveMethod(CkMethodHandlers.Pipeline)]
        });

        var operationResult = new OperationResult();
        Resolve(model, operationResult);

        Assert.Equal([131, 132], operationResult.Messages.Select(m => m.MessageNumber).OrderBy(n => n));
    }

    [Fact]
    public void Code132_InterfaceMethodWithSensitiveParameter_OK_AndImplementingTypeMustUseService()
    {
        var model = Model();
        (model.Interfaces ??= []).Add(new CkInterfaceDto
        {
            InterfaceId = "Secured-1",
            Methods = [SensitiveMethod(CkMethodHandlers.Service)]
        });
        var tag = Type(model, "Tag");
        tag.Implements = [$"{M}/Secured-1"];
        tag.Methods = [SensitiveMethod(CkMethodHandlers.Service)];
        ResolveExpectingNoMessages(model);

        // The implementing type redeclares the method as a pipeline: refused at the type.
        tag.Methods = [SensitiveMethod(CkMethodHandlers.Pipeline)];
        var message = Assert.Single(ResolveExpectingOnly(model, 132));
        Assert.Contains("/Tag", message.MessageText);
    }

    [Fact]
    public void Code132_InheritedSensitiveServiceMethod_OK_AndRefusedOnlyAtTheDeclaringType()
    {
        var model = Model();
        // Account derives from Principal (kitchen sink). The sensitive method is declared once on the base type.
        Type(model, "Principal").Methods!.Add(SensitiveMethod(CkMethodHandlers.Service));
        ResolveExpectingNoMessages(model);

        Type(model, "Principal").Methods!.Single(m => m.MethodId == "SetSecret-1").Execution!.Handler =
            CkMethodHandlers.Pipeline;
        var messages = ResolveExpectingOnly(model, 132);
        Assert.Single(messages);
        Assert.Contains("/Principal", messages[0].MessageText);
    }

    // ── sensitive result + pipeline handler (AB#7006, message 134) ───────────────────

    private static CkMethodDto TokenMethod(string? handler, AttributeValueTypesDto valueType, bool sensitive,
        int? timeoutSeconds = 30) => new()
    {
        MethodId = "IssueToken-1",
        Parameters = [new() { Name = "scope", ValueType = AttributeValueTypesDto.String }],
        Result = new()
        {
            ValueType = valueType,
            ValueCkRecordId = valueType == AttributeValueTypesDto.Record ? new CkId<CkRecordId>($"{M}/Address") : null,
            ValueCkEnumId = valueType == AttributeValueTypesDto.Enum ? new CkId<CkEnumId>($"{M}/Mode") : null,
            Sensitive = sensitive
        },
        Execution = handler == null && timeoutSeconds == null
            ? null
            : new() { Handler = handler, TimeoutSeconds = timeoutSeconds }
    };

    [Theory]
    [InlineData(AttributeValueTypesDto.String)]
    [InlineData(AttributeValueTypesDto.Enum)]
    [InlineData(AttributeValueTypesDto.Record)]
    public void Code134_SensitiveResultWithPipelineHandler_NamesMethodAndType_ForEveryResultKind(
        AttributeValueTypesDto valueType)
    {
        var model = Model();
        Type(model, "Tag").Methods = [TokenMethod(CkMethodHandlers.Pipeline, valueType, sensitive: true)];

        var message = Assert.Single(ResolveExpectingOnly(model, 134));
        Assert.Contains("IssueToken-1", message.MessageText);
        Assert.Contains("/Tag", message.MessageText);
        Assert.Contains("result is sensitive", message.MessageText);
        Assert.Contains("execution.handler: service", message.MessageText);
    }

    [Theory]
    [InlineData(CkMethodHandlers.Service)]
    [InlineData(null)]
    public void Code134_SensitiveResultWithServiceOrOmittedHandler_OK_AndReachesTheGraph(string? handler)
    {
        var model = Model();
        Type(model, "Tag").Methods =
            [TokenMethod(handler, AttributeValueTypesDto.String, sensitive: true, handler == null ? null : 30)];

        var operationResult = new OperationResult();
        var graph = Resolve(model, operationResult);

        Assert.Empty(operationResult.Messages);
        Assert.True(graph.Types[$"{M}/Tag"].AllMethods["IssueToken-1"].HasSensitiveResult);
        Assert.True(graph.Types[$"{M}/Tag"].AllMethods["IssueToken-1"].Definition.Result!.Sensitive);
        // Methods without the marker, or without a result, keep the default.
        Assert.False(graph.Types[$"{M}/Account"].AllMethods["Unlock-1"].HasSensitiveResult);
        Assert.False(graph.Types[$"{M}/Principal"].AllMethods["ChangePassword-1"].HasSensitiveResult);
    }

    [Fact]
    public void Code134_PipelineWithANonSensitiveResult_OK()
    {
        var model = Model();
        Type(model, "Tag").Methods = [TokenMethod(CkMethodHandlers.Pipeline, AttributeValueTypesDto.String, sensitive: false)];

        ResolveExpectingNoMessages(model);
    }

    [Fact]
    public void Code134_AndCode132_AreBothReportedWhenParameterAndResultAreSensitive()
    {
        var model = Model();
        var method = SensitiveMethod(CkMethodHandlers.Pipeline);
        method.Result = new() { ValueType = AttributeValueTypesDto.String, Sensitive = true };
        Type(model, "Tag").Methods = [method];

        var operationResult = new OperationResult();
        Resolve(model, operationResult);

        Assert.Equal([132, 134], operationResult.Messages.Select(m => m.MessageNumber).OrderBy(n => n));
    }

    [Fact]
    public void Code134_AndCode131_AreBothReportedForAPipelineWithoutTimeout()
    {
        var model = Model();
        Type(model, "Tag").Methods = [TokenMethod(CkMethodHandlers.Pipeline, AttributeValueTypesDto.String, true, null)];

        var operationResult = new OperationResult();
        Resolve(model, operationResult);

        Assert.Equal([131, 134], operationResult.Messages.Select(m => m.MessageNumber).OrderBy(n => n));
    }

    [Fact]
    public void Code134_InterfaceMethod_SensitiveResultIsAllowed_PipelineIsRefusedByBoth131And134()
    {
        var model = Model();
        (model.Interfaces ??= []).Add(new CkInterfaceDto
        {
            InterfaceId = "Tokens-1",
            Methods = [TokenMethod(CkMethodHandlers.Service, AttributeValueTypesDto.String, sensitive: true)]
        });
        ResolveExpectingNoMessages(model);

        model.Interfaces!.Single(i => i.InterfaceId.FullName == "Tokens-1").Methods![0] =
            TokenMethod(CkMethodHandlers.Pipeline, AttributeValueTypesDto.String, sensitive: true);
        var operationResult = new OperationResult();
        Resolve(model, operationResult);
        Assert.Equal([131, 134], operationResult.Messages.Select(m => m.MessageNumber).OrderBy(n => n));
    }

    [Fact]
    public void Code134_InterfaceSensitiveResult_ImplementingTypeMustUseService_AndKeepTheMarker()
    {
        var model = Model();
        (model.Interfaces ??= []).Add(new CkInterfaceDto
        {
            InterfaceId = "Tokens-1",
            Methods = [TokenMethod(CkMethodHandlers.Service, AttributeValueTypesDto.String, sensitive: true)]
        });
        var tag = Type(model, "Tag");
        tag.Implements = [$"{M}/Tokens-1"];
        tag.Methods = [TokenMethod(CkMethodHandlers.Service, AttributeValueTypesDto.String, sensitive: true)];
        ResolveExpectingNoMessages(model);

        // The implementing type redeclares the method as a pipeline: refused at the type (the contract is equal).
        tag.Methods = [TokenMethod(CkMethodHandlers.Pipeline, AttributeValueTypesDto.String, sensitive: true)];
        var message = Assert.Single(ResolveExpectingOnly(model, 134));
        Assert.Contains("/Tag", message.MessageText);
    }

    [Fact]
    public void Code122_RedeclaringAnInterfaceMethodWithoutTheSensitiveResultMarker_IsRefused()
    {
        var model = Model();
        (model.Interfaces ??= []).Add(new CkInterfaceDto
        {
            InterfaceId = "Tokens-1",
            Methods = [TokenMethod(CkMethodHandlers.Service, AttributeValueTypesDto.String, sensitive: true)]
        });
        var tag = Type(model, "Tag");
        tag.Implements = [$"{M}/Tokens-1"];
        tag.Methods = [TokenMethod(CkMethodHandlers.Service, AttributeValueTypesDto.String, sensitive: false)];

        var message = Assert.Single(ResolveExpectingOnly(model, 122));
        Assert.Contains("result sensitive", message.MessageText);
    }

    [Fact]
    public void Code134_InheritedSensitiveServiceMethod_OK_AndRefusedOnlyAtTheDeclaringType()
    {
        var model = Model();
        // Account derives from Principal (kitchen sink). The sensitive-result method is declared once on the base type.
        Type(model, "Principal").Methods!.Add(
            TokenMethod(CkMethodHandlers.Service, AttributeValueTypesDto.String, sensitive: true));
        var operationResult = new OperationResult();
        var graph = Resolve(model, operationResult);
        Assert.Empty(operationResult.Messages);
        Assert.True(graph.Types[$"{M}/Account"].AllMethods["IssueToken-1"].HasSensitiveResult);

        Type(model, "Principal").Methods!.Single(m => m.MethodId == "IssueToken-1").Execution!.Handler =
            CkMethodHandlers.Pipeline;
        var message = Assert.Single(ResolveExpectingOnly(model, 134));
        Assert.Contains("/Principal", message.MessageText);
    }

    // ── authorization.dataAccess (AB#7036, message 135) ──────────────────────────────

    [Fact]
    public void Code135_ReadOrWriteOnAMethod_OK_AndReachesTheGraphAsTheEffectiveValue()
    {
        var model = Model();
        Type(model, "Principal").Methods!.Single(m => m.MethodId == "ChangePassword-1").Authorization!.DataAccess =
            CkMethodDataAccessDto.Read;
        Type(model, "Account").Methods!.Single(m => m.MethodId == "Lock-1").Authorization =
            new CkMethodAuthorizationDto { Roles = ["Admin"], DataAccess = CkMethodDataAccessDto.Write };

        var operationResult = new OperationResult();
        var graph = Resolve(model, operationResult);

        Assert.Empty(operationResult.Messages);
        var methods = graph.Types[$"{M}/Account"].AllMethods;
        Assert.Equal(CkMethodDataAccessDto.Read, methods["ChangePassword-1"].DataAccess);
        Assert.Equal(CkMethodDataAccessDto.Write, methods["Lock-1"].DataAccess);
        // No authorization block at all, and a block without the key: Write.
        Assert.Equal(CkMethodDataAccessDto.Write, methods["Unlock-1"].DataAccess);
    }

    [Fact]
    public void Code135_ReadOnAMethodThatReplacesTheGenericCreate()
    {
        var model = Model();
        Type(model, "Tag").Methods = [new()
        {
            MethodId = "Make-1", Kind = CkMethodKindDto.Static, ReplacesGenericCreate = true,
            Authorization = new() { Roles = ["Admin"], DataAccess = CkMethodDataAccessDto.Read }
        }];

        var message = Assert.Single(ResolveExpectingOnly(model, 135));
        Assert.Contains("Make-1", message.MessageText);
        Assert.Contains("creating an entity is a write", message.MessageText);
    }

    [Fact]
    public void Code135_WriteOnAMethodThatReplacesTheGenericCreate_OK()
    {
        var model = Model();
        Type(model, "Tag").Methods = [new()
        {
            MethodId = "Make-1", Kind = CkMethodKindDto.Static, ReplacesGenericCreate = true,
            Authorization = new() { Roles = ["Admin"], DataAccess = CkMethodDataAccessDto.Write }
        }];

        var operationResult = new OperationResult();
        Resolve(model, operationResult);
        Assert.Empty(operationResult.Messages);
    }

    [Fact]
    public void Code135_AValueOutsideReadAndWrite_FromAJsonOrProgrammaticModel()
    {
        var model = Model();
        Type(model, "Principal").Methods!.Single(m => m.MethodId == "ChangePassword-1").Authorization!.DataAccess =
            (CkMethodDataAccessDto)7;

        var message = Assert.Single(ResolveExpectingOnly(model, 135));
        Assert.Contains("ChangePassword-1", message.MessageText);
        Assert.Contains("'Read' or 'Write'", message.MessageText);
    }

    // ── replacesGenericCreate (AB#7024, message 133) ─────────────────────────────────

    [Fact]
    public void Code133_StaticMethodOnAConcreteType_OK_AndReachesTheGraph()
    {
        var model = Model();
        Type(model, "Account").Methods!.Single(m => m.MethodId == "Lock-1").ReplacesGenericCreate = true;

        var operationResult = new OperationResult();
        var graph = Resolve(model, operationResult);

        Assert.Empty(operationResult.Messages);
        Assert.True(graph.Types[$"{M}/Account"].AllMethods["Lock-1"].ReplacesGenericCreate);
        // Methods that do not set the flag keep the default.
        Assert.False(graph.Types[$"{M}/Account"].AllMethods["Unlock-1"].ReplacesGenericCreate);
    }

    [Fact]
    public void Code133_InstanceMethod()
    {
        var model = Model();
        Type(model, "Tag").Methods = [new() { MethodId = "Ping-1", ReplacesGenericCreate = true }];

        var message = Assert.Single(ResolveExpectingOnly(model, 133));
        Assert.Contains("Ping-1", message.MessageText);
        Assert.Contains("Static", message.MessageText);
    }

    [Fact]
    public void Code133_InterfaceMethod()
    {
        var model = Model();
        (model.Interfaces ??= []).Add(new CkInterfaceDto
        {
            InterfaceId = "Creatable-1",
            Methods = [new() { MethodId = "Make-1", Kind = CkMethodKindDto.Static, ReplacesGenericCreate = true }]
        });

        var message = Assert.Single(ResolveExpectingOnly(model, 133));
        Assert.Contains("Make-1", message.MessageText);
        Assert.Contains("interface method", message.MessageText);
    }

    [Fact]
    public void Code133_AbstractType()
    {
        var model = Model();
        Type(model, "Principal").Methods!.Add(new CkMethodDto
        {
            MethodId = "Provision-1", Kind = CkMethodKindDto.Static, ReplacesGenericCreate = true
        });

        var message = Assert.Single(ResolveExpectingOnly(model, 133));
        Assert.Contains("Provision-1", message.MessageText);
        Assert.Contains("abstract", message.MessageText);
    }

    [Fact]
    public void Code133_SecondMethodOnTheSameType_NamesBothMethods()
    {
        var model = Model();
        Type(model, "Tag").Methods =
        [
            new() { MethodId = "Make-1", Kind = CkMethodKindDto.Static, ReplacesGenericCreate = true },
            new() { MethodId = "Build-1", Kind = CkMethodKindDto.Static, ReplacesGenericCreate = true }
        ];

        var messages = ResolveExpectingOnly(model, 133);
        Assert.Equal(2, messages.Count);
        Assert.All(messages, m => Assert.Contains("at most one", m.MessageText));
        Assert.All(messages, m => Assert.Contains($"{M}/Tag.Build-1", m.MessageText));
        Assert.All(messages, m => Assert.Contains($"{M}/Tag.Make-1", m.MessageText));
    }

    [Fact]
    public void Code133_SecondMethodInheritedFromABaseType_IsReportedOnTheDerivedTypeOnly()
    {
        var model = Model();
        Type(model, "Account").Methods!.Single(m => m.MethodId == "Lock-1").ReplacesGenericCreate = true;
        model.Types!.Add(new CkCompiledTypeDto
        {
            TypeId = "Savings",
            DerivedFromCkTypeId = $"{M}/Account",
            Methods = [new() { MethodId = "Open-1", Kind = CkMethodKindDto.Static, ReplacesGenericCreate = true }]
        });

        var message = Assert.Single(ResolveExpectingOnly(model, 133));
        Assert.Contains("Open-1", message.MessageText);
        Assert.Contains($"{M}/Savings", message.MessageText);
        Assert.Contains($"{M}/Account.Lock-1", message.MessageText);
    }

    [Fact]
    public void Code133_DerivedTypeInheritingTheFlaggedMethod_OK_AndSeesTheFlag()
    {
        var model = Model();
        Type(model, "Account").Methods!.Single(m => m.MethodId == "Lock-1").ReplacesGenericCreate = true;
        model.Types!.Add(new CkCompiledTypeDto { TypeId = "Savings", DerivedFromCkTypeId = $"{M}/Account" });

        var operationResult = new OperationResult();
        var graph = Resolve(model, operationResult);

        Assert.Empty(operationResult.Messages);
        var inherited = graph.Types[$"{M}/Savings"].AllMethods["Lock-1"];
        Assert.True(inherited.ReplacesGenericCreate);
        Assert.Equal($"{M}/Account.Lock-1", inherited.QualifiedMethodId);
    }

    [Fact]
    public void Code90_FlagInACkLanguage1Model()
    {
        var model = Model();
        model.CkLanguage = 1;
        Type(model, "Tag").Methods = [new() { MethodId = "Make-1", Kind = CkMethodKindDto.Static, ReplacesGenericCreate = true }];

        var operationResult = new OperationResult();
        Resolve(model, operationResult);

        Assert.Contains(operationResult.Messages, m => m.MessageNumber == 90 && m.MessageText.Contains("replacesGenericCreate"));
    }
}

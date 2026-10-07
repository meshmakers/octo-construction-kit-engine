using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.CkV2;

/// <summary>
///     CK v2 (AB#5584) fixtures: the SemVer test model with every CK v2 member set.
/// </summary>
internal static class CkV2TestModels
{
    public const string ModelName = SemVerTestModels.ModelName;

    public static CkMethodDto CreateFullMethod()
    {
        return new CkMethodDto
        {
            MethodId = "ChangePassword-1",
            Kind = CkMethodKindDto.Instance,
            Description = "Changes the password",
            Parameters =
            [
                new CkMethodParameterDto
                {
                    Name = "currentPassword", ValueType = AttributeValueTypesDto.String, IsOptional = true,
                    Sensitive = true, Description = "The current password"
                },
                new CkMethodParameterDto
                {
                    Name = "state", ValueType = AttributeValueTypesDto.Enum, ValueCkEnumId = $"{ModelName}/State"
                },
                new CkMethodParameterDto
                {
                    Name = "address", ValueType = AttributeValueTypesDto.Record,
                    ValueCkRecordId = $"{ModelName}/Address"
                }
            ],
            Result = new CkMethodResultDto
            {
                ValueType = AttributeValueTypesDto.Record, ValueCkRecordId = $"{ModelName}/Address"
            },
            Errors =
            [
                new CkMethodErrorDto { Code = "PASSWORD_POLICY_VIOLATION", Description = "Policy violated" },
                new CkMethodErrorDto { Code = "USER_LOCKED_OUT" }
            ],
            Authorization = new CkMethodAuthorizationDto
            {
                Roles = ["UserManagement"], AllowSelf = true, Scopes = ["extra_scope"]
            },
            Execution = new CkMethodExecutionDto { TimeoutSeconds = 30, Idempotent = true }
        };
    }

    /// <summary>
    ///     The SemVer test model with ckLanguage 2, an interface, implements, a full and a minimal method and access
    ///     on a type and a record assignment.
    /// </summary>
    public static CkCompiledModelRoot CreateModel(string version = "1.0.0")
    {
        var model = SemVerTestModels.CreateModel(version);
        model.CkLanguage = 2;
        model.Interfaces =
        [
            new CkInterfaceDto
            {
                InterfaceId = "Serialized-1",
                Description = "Has a serial number",
                Attributes =
                [
                    new CkInterfaceAttributeDto { CkAttributeId = $"{ModelName}/SerialNumber", AttributeName = "SerialNumber" },
                    new CkInterfaceAttributeDto
                    {
                        CkAttributeId = $"{ModelName}/StateAttr", AttributeName = "State", IsOptional = true
                    }
                ]
            }
        ];
        var machine = SemVerTestModels.GetMachine(model);
        machine.Implements = [new CkId<CkInterfaceId>($"{ModelName}/Serialized-1")];
        machine.Methods = [CreateFullMethod(), new CkMethodDto { MethodId = "Reset-1" }];
        machine.Attributes!.Single(a => a.AttributeName == "State").Access = CkAttributeAccessDto.Hidden;
        SemVerTestModels.GetRecord(model).Attributes!.Single().Access = CkAttributeAccessDto.ReadOnly;
        return model;
    }
}

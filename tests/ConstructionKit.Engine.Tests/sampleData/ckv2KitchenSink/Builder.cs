using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.sampleData.ckv2KitchenSink;

/// <summary>
///     CK v2 Phase 0 kitchen-sink model (ckLanguage 2), built in C#: two interfaces (one with a required and an
///     optional member, one with a single member), an abstract base implementing Named-1, a derived type inheriting
///     Named-1 and implementing Serialized-1, an unrelated type implementing Named-1, all four access values on type
///     and record assignments, and one method with every field set plus one with only a method id.
/// </summary>
public static class Builder
{
    public const string ModelName = "ckv2KitchenSink";

    public static CkMethodDto FullMethod() => new()
    {
        MethodId = "ChangePassword-1",
        Kind = CkMethodKindDto.Instance,
        Description = "Changes (self) or resets (UserManagement) the password",
        Parameters =
        [
            new() { Name = "currentPassword", ValueType = AttributeValueTypesDto.String, IsOptional = true, Sensitive = true },
            new() { Name = "newPassword", ValueType = AttributeValueTypesDto.String, Sensitive = true, Description = "The new password" },
            new() { Name = "mode", ValueType = AttributeValueTypesDto.Enum, ValueCkEnumId = $"{ModelName}/Mode" },
            new() { Name = "address", ValueType = AttributeValueTypesDto.Record, ValueCkRecordId = $"{ModelName}/Address" }
        ],
        Result = new() { ValueType = AttributeValueTypesDto.Record, ValueCkRecordId = $"{ModelName}/Address" },
        Errors =
        [
            new() { Code = "PASSWORD_POLICY_VIOLATION", Description = "The password violates the policy" },
            new() { Code = "CURRENT_PASSWORD_INVALID" },
            new() { Code = "USER_LOCKED_OUT" }
        ],
        Authorization = new() { Roles = ["UserManagement"], AllowSelf = true, Scopes = ["extra"] },
        Execution = new() { TimeoutSeconds = 15, Idempotent = false }
    };

    public static CkCompiledModelRoot Build()
    {
        return new CkCompiledModelRoot
        {
            ModelId = new CkModelId(ModelName, "1.0.0"),
            CkLanguage = 2,
            Dependencies = [new("System", "1.0.0")],
            Attributes =
            [
                new() { AttributeId = "Name", ValueType = AttributeValueTypesDto.String },
                new() { AttributeId = "Description", ValueType = AttributeValueTypesDto.String },
                new() { AttributeId = "Serial", ValueType = AttributeValueTypesDto.String },
                new() { AttributeId = "PasswordHash", ValueType = AttributeValueTypesDto.String },
                new() { AttributeId = "Status", ValueType = AttributeValueTypesDto.String },
                new() { AttributeId = "Street", ValueType = AttributeValueTypesDto.String },
                new() { AttributeId = "City", ValueType = AttributeValueTypesDto.String },
                new() { AttributeId = "Address", ValueType = AttributeValueTypesDto.Record, ValueCkRecordId = $"{ModelName}/Address" }
            ],
            Enums =
            [
                new() { EnumId = "Mode", Values = [new() { Key = 0, Name = "Change" }, new() { Key = 1, Name = "Reset" }] }
            ],
            Records =
            [
                new()
                {
                    RecordId = "Address",
                    Attributes =
                    [
                        new() { CkAttributeId = $"{ModelName}/Street", AttributeName = "Street", IsOptional = true, Access = CkAttributeAccessDto.ReadOnly },
                        new() { CkAttributeId = $"{ModelName}/City", AttributeName = "City", IsOptional = true, Access = CkAttributeAccessDto.Hidden }
                    ]
                }
            ],
            Interfaces =
            [
                new()
                {
                    InterfaceId = "Named-1",
                    Description = "Anything with a human-readable name",
                    Attributes =
                    [
                        new() { CkAttributeId = $"{ModelName}/Name", AttributeName = "Name" },
                        new() { CkAttributeId = $"{ModelName}/Description", AttributeName = "Description", IsOptional = true }
                    ]
                },
                new()
                {
                    InterfaceId = "Serialized-1",
                    Attributes = [new() { CkAttributeId = $"{ModelName}/Serial", AttributeName = "Serial" }]
                }
            ],
            Types =
            [
                new()
                {
                    TypeId = "Principal",
                    IsAbstract = true,
                    DerivedFromCkTypeId = "System/Entity",
                    Implements = [$"{ModelName}/Named-1"],
                    Attributes =
                    [
                        new() { CkAttributeId = $"{ModelName}/Name", AttributeName = "Name", Access = CkAttributeAccessDto.ReadWrite },
                        new() { CkAttributeId = $"{ModelName}/Status", AttributeName = "Status", IsOptional = true, Access = CkAttributeAccessDto.MethodOnly }
                    ],
                    Methods = [FullMethod(), new() { MethodId = "Unlock-1" }]
                },
                new()
                {
                    TypeId = "Account",
                    DerivedFromCkTypeId = $"{ModelName}/Principal",
                    Implements = [$"{ModelName}/Serialized-1"],
                    Attributes =
                    [
                        new() { CkAttributeId = $"{ModelName}/Serial", AttributeName = "Serial", Access = CkAttributeAccessDto.ReadOnly },
                        new() { CkAttributeId = $"{ModelName}/PasswordHash", AttributeName = "PasswordHash", IsOptional = true, Access = CkAttributeAccessDto.Hidden }
                    ],
                    Methods = [new() { MethodId = "Lock-1", Kind = CkMethodKindDto.Static }]
                },
                new()
                {
                    TypeId = "Tag",
                    DerivedFromCkTypeId = "System/Entity",
                    Implements = [$"{ModelName}/Named-1"],
                    Attributes =
                    [
                        new() { CkAttributeId = $"{ModelName}/Name", AttributeName = "Name" },
                        new() { CkAttributeId = $"{ModelName}/Description", AttributeName = "Description", IsOptional = true }
                    ]
                }
            ]
        };
    }
}

using System.Collections;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;
using Meshmakers.Octo.Runtime.Engine.Tests.Fixtures;

namespace Meshmakers.Octo.Runtime.Engine.Tests;

public class RtPathEvaluatorTests(CacheServiceFixture fixture) : IClassFixture<CacheServiceFixture>
{
    #region TokenizePath

    [Fact]
    public void TokenizePath_Properties_OK()
    {
        var result = RtPathEvaluator.TokenizePath("property1.property2.property3");
        Assert.Equal(3, result.Count);
        Assert.Equal("property1", result[0].Value);
        Assert.Equal(PathType.Attribute, result[0].Type);
        Assert.Equal("property2", result[1].Value);
        Assert.Equal(PathType.Attribute, result[1].Type);
        Assert.Equal("property3", result[2].Value);
        Assert.Equal(PathType.Attribute, result[2].Type);
    }

    [Fact]
    public void TokenizePath_Properties_NavigationProperties_OK()
    {
        var result = RtPathEvaluator.TokenizePath("property1.property2->property3");
        Assert.Equal(3, result.Count);
        Assert.Equal("property1", result[0].Value);
        Assert.Equal(PathType.Navigation, result[0].Type);
        Assert.Equal("property2", result[1].Value);
        Assert.Equal(PathType.TargetCkTypeId, result[1].Type);
        Assert.Equal("property3", result[2].Value);
        Assert.Equal(PathType.Attribute, result[2].Type);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("5")]
    [InlineData("*")]
    public void TokenizePath_ArrayIndex_OK(string index)
    {
        var result = RtPathEvaluator.TokenizePath($"property1[{index}].property2.property3");
        Assert.Equal(4, result.Count);
        Assert.Equal("property1", result[0].Value);
        Assert.Equal(PathType.Attribute, result[0].Type);
        Assert.Equal(index, result[1].Value);
        Assert.Equal(PathType.ArrayIndex, result[1].Type);
        Assert.Equal("property2", result[2].Value);
        Assert.Equal(PathType.Attribute, result[2].Type);
        Assert.Equal("property3", result[3].Value);
        Assert.Equal(PathType.Attribute, result[3].Type);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("5")]
    [InlineData("*")]
    public void TokenizePath_ArrayIndex_NavigationProperty_OK(string index)
    {
        var result = RtPathEvaluator.TokenizePath($"property1.property2->property3[{index}]");
        Assert.Equal(4, result.Count);
        Assert.Equal("property1", result[0].Value);
        Assert.Equal(PathType.Navigation, result[0].Type);

        Assert.Equal("property2", result[1].Value);
        Assert.Equal(PathType.TargetCkTypeId, result[1].Type);

        Assert.Equal("property3", result[2].Value);
        Assert.Equal(PathType.Attribute, result[2].Type);

        Assert.Equal(index, result[3].Value);
        Assert.Equal(PathType.ArrayIndex, result[3].Type);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("$")]
    public void TokenizePath_ArrayIndex_Fail(string index)
    {
        Assert.Throws<InvalidPathException>(() =>
            RtPathEvaluator.TokenizePath($"property1[{index}].property2.property3"));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("$")]
    public void TokenizePath_ArrayIndex_NavigationProperty_Fail(string index)
    {
        Assert.Throws<InvalidPathException>(() =>
            RtPathEvaluator.TokenizePath($"property1[{index}]->property2.property3"));
    }

    [Fact]
    public void TokenizePath_TargetCkTypeIdWithoutNavigationProperty_Fail()
    {
        Assert.Throws<InvalidPathException>(() =>
            RtPathEvaluator.TokenizePath("myNavPropName->Record.InnerString"));
    }

    [Fact]
    public void TokenizePath_NavigationProperty_NavigationProperty_OK()
    {
        var result = RtPathEvaluator.TokenizePath("nav1.type1->nav2.type2->property1");

        Assert.Equal(5, result.Count);
        Assert.Equal("nav1", result[0].Value);
        Assert.Equal(PathType.Navigation, result[0].Type);

        Assert.Equal("type1", result[1].Value);
        Assert.Equal(PathType.TargetCkTypeId, result[1].Type);

        Assert.Equal("nav2", result[2].Value);
        Assert.Equal(PathType.Navigation, result[2].Type);

        Assert.Equal("type2", result[3].Value);
        Assert.Equal(PathType.TargetCkTypeId, result[3].Type);

        Assert.Equal("property1", result[4].Value);
        Assert.Equal(PathType.Attribute, result[4].Type);
    }

    [Fact]
    public void TokenizePath_NavigationProperty_NavigationProperty_NavigationProperty_OK()
    {
        var result = RtPathEvaluator.TokenizePath("nav1.type1->nav2.type2->nav3.type3->property1");

        Assert.Equal(7, result.Count);
        Assert.Equal("nav1", result[0].Value);
        Assert.Equal(PathType.Navigation, result[0].Type);

        Assert.Equal("type1", result[1].Value);
        Assert.Equal(PathType.TargetCkTypeId, result[1].Type);

        Assert.Equal("nav2", result[2].Value);
        Assert.Equal(PathType.Navigation, result[2].Type);

        Assert.Equal("type2", result[3].Value);
        Assert.Equal(PathType.TargetCkTypeId, result[3].Type);

        Assert.Equal("nav3", result[4].Value);
        Assert.Equal(PathType.Navigation, result[4].Type);

        Assert.Equal("type3", result[5].Value);
        Assert.Equal(PathType.TargetCkTypeId, result[5].Type);

        Assert.Equal("property1", result[6].Value);
        Assert.Equal(PathType.Attribute, result[6].Type);
    }

    #endregion TokenizePath

    #region GetValue

    private class SystemDataGenerator : IEnumerable<object[]>
    {
        private readonly List<object[]> _data =
        [
            new object[] { "RtId", OctoObjectId.Parse("68259915225146801209c9f1") },
            new object[] { "RtWellKnownName", "ExpectedWellknownName" },
            new object[] { "RtVersion", (ulong)5 },
            new object[] { "RtCreationDateTime", new DateTime(2021, 10, 9, 8, 7, 6) },
            new object[] { "RtChangedDateTime", new DateTime(2020, 9, 8, 7, 6, 5) }
        ];

        public IEnumerator<object[]> GetEnumerator() => _data.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    [Theory]
    [ClassData(typeof(SystemDataGenerator))]
    public async Task GetValue_SystemAttributes_OK(string attributePath, object expectedValue)
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            { "IsEnabled", true }
        };

        var rtId = OctoObjectId.Parse("68259915225146801209c9f1");
        var rtEntity = new RtEntity("Test/Sensor", rtId, d)
        {
            RtWellKnownName = "ExpectedWellknownName",
            RtVersion = 5,
            RtCreationDateTime = new DateTime(2021, 10, 9, 8, 7, 6),
            RtChangedDateTime = new DateTime(2020, 9, 8, 7, 6, 5),
        };

        var result = RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity, attributePath);
        Assert.Equal(expectedValue, result);
    }

    private class NavigationSystemDataGenerator : IEnumerable<object[]>
    {
        private readonly List<object[]> _data =
        [
            new object[] { "myNavPropName.testSensor->RtId", OctoObjectId.Parse("68259915225146801209c9f1") },
            new object[] { "myNavPropName.testSensor->RtWellKnownName", "ExpectedWellknownName" },
            new object[] { "myNavPropName.testSensor->RtVersion", (ulong)867 },
            new object[] { "myNavPropName.testSensor->RtCreationDateTime", new DateTime(2022, 10, 9, 8, 7, 6) },
            new object[] { "myNavPropName.testSensor->RtChangedDateTime", new DateTime(2023, 9, 8, 7, 6, 5) }
        ];

        public IEnumerator<object[]> GetEnumerator() => _data.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }


    [Theory]
    [ClassData(typeof(NavigationSystemDataGenerator))]
    public async Task GetValue_Navigation_SystemAttributes_OK(string attributePath, object expectedValue)
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            { "IsEnabled", true },
            { "DataCount", 42 },
            {
                "RecordTest", new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                {
                    { "Designation", "DemoValueInnerString" }
                })
            }
        };

        var d2 = new Dictionary<string, object?>
        {
            { "Designation", "Child item text" },
            { "DataCount", 49 }
        };

        var associations = new List<NavigationEnd>
        {
            new()
            {
                AssociationId = OctoObjectId.GenerateNewId(),
                AssociationRoleId = new CkId<CkAssociationRoleId>("System/ParentChild"),
                TargetCkTypeId = "Test/Sensor",
                NavigationPropertyName = "MyNavPropName",
                Targets = [new RtEntity("Test/Sensor", OctoObjectId.Parse("68259915225146801209c9f1"), d2)        {
                    RtWellKnownName = "ExpectedWellknownName",
                    RtVersion = 867,
                    RtCreationDateTime = new DateTime(2022, 10, 9, 8, 7, 6),
                    RtChangedDateTime = new DateTime(2023, 9, 8, 7, 6, 5),
                }]
            }
        };

        var rtEntity = new RtEntityGraphItem("Test/Zone", OctoObjectId.GenerateNewId(), d, associations);

        var result = RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity, attributePath);
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public async Task GetValue_SimpleStringValue_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            { "IsEnabled", true }
        };

        var rtEntity = new RtEntity("Test/Sensor", OctoObjectId.GenerateNewId(), d);

        var result = RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity, "Designation");
        Assert.Equal("Test", result);
    }

    [Fact]
    public async Task GetValue_SimpleBoolValue_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            { "IsEnabled", true }
        };

        var rtEntity = new RtEntity("Test/Sensor", OctoObjectId.GenerateNewId(), d);

        var result = RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity, "IsEnabled");
        Assert.Equal(true, result);
    }

    [Fact]
    public async Task GetValue_Record_StringValue_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            { "IsEnabled", true },
            { "DataCount", 42 },
            {
                "RecordTest", new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                {
                    { "Designation", "DemoValueInnerString" }
                })
            }
        };

        var rtEntity = new RtEntity("Test/Sensor", OctoObjectId.GenerateNewId(), d);

        var result = RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity, "RecordTest.Designation");
        Assert.Equal("DemoValueInnerString", result);
    }

    [Fact]
    public async Task GetValue_RecordArray_Index_StringValue_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            { "IsEnabled", true },
            { "DataCount", 42 },
            {
                "RecordArrayTests", new[]
                {
                    new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                    {
                        { "Designation", "DemoValueInnerString" }
                    })
                }
            }
        };

        var rtEntity = new RtEntity("Test/Sensor", OctoObjectId.GenerateNewId(), d);

        var result = RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity,
            "RecordArrayTests[0].Designation");
        Assert.Equal("DemoValueInnerString", result);
    }

    [Fact]
    public async Task GetValue_RecordArray_Index_StringValue_NotExisting_Fail()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            { "IsEnabled", true },
            { "DataCount", 42 },
            {
                "RecordArrayTests", new[]
                {
                    new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                    {
                        { "Designation", "DemoValueInnerString" }
                    })
                }
            }
        };

        var rtEntity = new RtEntity("Test/Sensor", OctoObjectId.GenerateNewId(), d);

        Assert.Throws<InvalidPathException>(() =>
            RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity, "RecordArrayTests[5].Designation"));
    }

    [Fact]
    public async Task GetValue_RecordArray_Last_StringValue_NotExisting_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            { "IsEnabled", true },
            { "DataCount", 42 },
            {
                "RecordArrayTests", new Dictionary<string, object>()
            }
        };

        var rtEntity = new RtEntity("Test/Sensor", OctoObjectId.GenerateNewId(), d);

        Assert.Throws<InvalidPathException>(() =>
            RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity,
                "RecordArrayTests[-1].Designation"));
    }

    [Fact]
    public async Task GetValue_RecordArray_Last_StringValue_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            { "IsEnabled", true },
            { "DataCount", 42 },
            {
                "RecordArrayTests", new[]
                {
                    new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                    {
                        { "Designation", "DemoValueInnerString0" }
                    }),
                    new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                    {
                        { "Designation", "DemoValueInnerString1" }
                    }),
                    new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                    {
                        { "Designation", "DemoValueInnerString2" }
                    })
                }
            }
        };

        var rtEntity = new RtEntity("Test/Sensor", OctoObjectId.GenerateNewId(), d);

        var result = RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity, "RecordArrayTests[-1].Designation");
        Assert.Equal("DemoValueInnerString2", result);
    }

    [Fact]
    public async Task GetValue_RecordArray_Wildcard_StringValue_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            { "IsEnabled", true },
            { "DataCount", 42 },
            {
                "RecordArrayTests", new[]
                {
                    new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                    {
                        { "Designation", "DemoValueInnerString0" }
                    }),
                    new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                    {
                        { "Designation", "DemoValueInnerString1" }
                    }),
                    new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                    {
                        { "Designation", "DemoValueInnerString2" }
                    })
                }
            }
        };

        var rtEntity = new RtEntity("Test/Sensor", OctoObjectId.GenerateNewId(), d);

        var result = RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity, "RecordArrayTests[*].Designation");
        Assert.Equal(["DemoValueInnerString0", "DemoValueInnerString1", "DemoValueInnerString2"],
            (IEnumerable<object>?)result);
    }

    [Fact]
    public async Task GetValue_IntArray_Wildcard_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            { "IsEnabled", true },
            {
                "IntArrayTests", new[]
                {
                    4, 5, 6, 7
                }
            }
        };

        var rtEntity = new RtEntity("Test/Sensor", OctoObjectId.GenerateNewId(), d);

        var result = RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity, "IntArrayTests[*]");
        Assert.Equal([4, 5, 6, 7], (IEnumerable<object>?)result);
    }

    [Fact]
    public async Task GetValue_Navigation_Record_StringValue_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" }
        };

        var d2 = new Dictionary<string, object?>
        {
            { "Designation", "Child item text" },
            { "DataCount", 49 },
            {
                "RecordTest", new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                {
                    { "Designation", "Navigation-DemoValueInnerString" }
                })
            }
        };

        var associations = new List<NavigationEnd>
        {
            new()
            {
                AssociationId = OctoObjectId.GenerateNewId(),
                AssociationRoleId = new CkId<CkAssociationRoleId>("System/ParentChild"),
                TargetCkTypeId = "Test/Sensor",
                Targets = [new RtEntity("Test/Sensor", OctoObjectId.GenerateNewId(), d2)],
                NavigationPropertyName = "MyNavPropName"
            }
        };

        var rtEntity = new RtEntityGraphItem("Test/Zone", OctoObjectId.GenerateNewId(), d, associations);

        var result = RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity,
            "myNavPropName.testSensor->RecordTest.Designation");
        Assert.Equal("Navigation-DemoValueInnerString", result);
    }

    [Fact]
    public async Task GetValue_Navigation_StringValue_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            { "IsEnabled", true },
            { "DataCount", 42 },
            {
                "RecordTest", new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                {
                    { "Designation", "DemoValueInnerString" }
                })
            }
        };

        var d2 = new Dictionary<string, object?>
        {
            { "Designation", "Child item text" },
            { "DataCount", 49 }
        };

        var associations = new List<NavigationEnd>
        {
            new()
            {
                AssociationId = OctoObjectId.GenerateNewId(),
                AssociationRoleId = new CkId<CkAssociationRoleId>("System/ParentChild"),
                TargetCkTypeId = "Test/Sensor",
                NavigationPropertyName = "MyNavPropName",
                Targets = [new RtEntity("Test/Sensor", OctoObjectId.GenerateNewId(), d2)]
            }
        };

        var rtEntity = new RtEntityGraphItem("Test/Zone", OctoObjectId.GenerateNewId(), d, associations);

        var result = RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity,
            "myNavPropName.testSensor->Designation");
        Assert.Equal("Child item text", result);
    }

    [Fact]
    public async Task GetValue_Navigation_NotExistingStringValue_Fail()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            { "IsEnabled", true },
            { "DataCount", 42 },
            {
                "RecordTest", new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                {
                    { "Designation", "DemoValueInnerString" }
                })
            }
        };

        var d2 = new Dictionary<string, object?>
        {
            { "Designation", "Child item text" },
            { "DataCount", 49 }
        };

        var associations = new List<NavigationEnd>
        {
            new()
            {
                AssociationId = OctoObjectId.GenerateNewId(),
                AssociationRoleId = new CkId<CkAssociationRoleId>("System/ParentChild"),
                TargetCkTypeId = "Test/Sensor",
                NavigationPropertyName = "MyNavPropName",
                Targets = [new RtEntity("Test/Sensor", OctoObjectId.GenerateNewId(), d2)]
            }
        };

        var rtEntity = new RtEntityGraphItem("Test/Zone", OctoObjectId.GenerateNewId(), d, associations);

        Assert.Throws<InvalidPathException>(() => RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity,
            "myNavPropName.testSensor->NotExisting"));
    }

    [Fact]
    public async Task GetValue_NotExistingNavigation_Fail()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            { "IsEnabled", true },
            { "DataCount", 42 },
            {
                "RecordTest", new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                {
                    { "Designation", "DemoValueInnerString" }
                })
            }
        };

        var d2 = new Dictionary<string, object?>
        {
            { "Designation", "Child item text" },
            { "DataCount", 49 }
        };

        var associations = new List<NavigationEnd>
        {
            new()
            {
                AssociationId = OctoObjectId.GenerateNewId(),
                AssociationRoleId = new CkId<CkAssociationRoleId>("System/ParentChild"),
                TargetCkTypeId = "Test/Sensor",
                NavigationPropertyName = "MyNavPropName",
                Targets = [new RtEntity("Test/Sensor", OctoObjectId.GenerateNewId(), d2)]
            }
        };

        var rtEntity = new RtEntityGraphItem("Test/Zone", OctoObjectId.GenerateNewId(), d, associations);

        Assert.Throws<InvalidPathException>(() => RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity,
            "myNavPropNameNotExisting.testSensor->Designation"));
    }

    [Fact]
    public async Task GetValue_NotExistingNavigationType_Fail()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            { "IsEnabled", true },
            { "DataCount", 42 },
            {
                "RecordTest", new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                {
                    { "Designation", "DemoValueInnerString" }
                })
            }
        };

        var d2 = new Dictionary<string, object?>
        {
            { "Designation", "Child item text" },
            { "DataCount", 49 }
        };

        var associations = new List<NavigationEnd>
        {
            new()
            {
                AssociationId = OctoObjectId.GenerateNewId(),
                AssociationRoleId = new CkId<CkAssociationRoleId>("System/ParentChild"),
                TargetCkTypeId = "Test/Sensor",
                NavigationPropertyName = "MyNavPropName",
                Targets = [new RtEntity("Test/Sensor", OctoObjectId.GenerateNewId(), d2)]
            }
        };

        var rtEntity = new RtEntityGraphItem("Test/Zone", OctoObjectId.GenerateNewId(), d, associations);

        Assert.Throws<InvalidPathException>(() => RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity,
            "myNavPropName.testSensorNotExisting->Designation"));
    }

    [Fact]
    public async Task GetValue_Navigation_NoTarget_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            { "IsEnabled", true },
            { "DataCount", 42 },
            {
                "RecordTest", new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                {
                    { "Designation", "DemoValueInnerString" }
                })
            }
        };

        var associations = new List<NavigationEnd>
        {
            new()
            {
                AssociationId = OctoObjectId.GenerateNewId(),
                AssociationRoleId = new CkId<CkAssociationRoleId>("System/ParentChild"),
                TargetCkTypeId = "Test/Sensor",
                NavigationPropertyName = "MyNavPropName",
                Targets = []
            }
        };

        var rtEntity = new RtEntityGraphItem("Test/Zone", OctoObjectId.GenerateNewId(), d, associations);

        var result = RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity,
            "myNavPropName.testSensor->Designation");

        Assert.Null(result);
    }

    #endregion GetValue

    #region SetValue

    [Fact]
    public async Task SetValue_SimpleStringValue_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" }
        };

        var rtEntity = new RtEntity("Test/City", OctoObjectId.GenerateNewId(), d);

        RtPathEvaluator.SetValue(ckCacheService, fixture.TenantId, rtEntity, "Designation", "new name of test");
        Assert.Equal("new name of test", rtEntity.GetAttributeStringValue("Designation"));
    }

    [Fact]
    public async Task SetValue_SimpleBoolValue_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Name", "Test" },
            { "IsEnabled", true }
        };

        var rtEntity = new RtEntity("Test/Sensor", OctoObjectId.GenerateNewId(), d);

        RtPathEvaluator.SetValue(ckCacheService, fixture.TenantId, rtEntity, "IsEnabled", false);
        Assert.False(rtEntity.GetAttributeValue<bool>("IsEnabled"));
    }

    [Fact]
    public async Task SetValue_Record_StringValue_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            { "IsEnabled", true },
            { "DataCount", 42 },
            {
                "RecordTest", new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                {
                    { "Designation", "DemoValueInnerString" }
                })
            }
        };

        var rtEntity = new RtEntity("Test/Sensor", OctoObjectId.GenerateNewId(), d);

        RtPathEvaluator.SetValue(ckCacheService, fixture.TenantId, rtEntity, "RecordTest.Designation",
            "new name of inner string value");
        Assert.Equal("new name of inner string value",
            rtEntity.GetAttributeValueByAccessPath(ckCacheService, fixture.TenantId, "RecordTest.Designation"));
    }

    [Fact]
    public async Task SetValue_RecordArray_Index_StringValue_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            {
                "RecordArrayTests", new[]
                {
                    new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                    {
                        { "Designation", "DemoValueInnerString" }
                    })
                }
            }
        };

        var rtEntity = new RtEntity("Test/Country", OctoObjectId.GenerateNewId(), d);

        RtPathEvaluator.SetValue(ckCacheService, fixture.TenantId, rtEntity, "RecordArrayTests[0].Designation",
            "new name of inner string value");
        Assert.Equal("new name of inner string value",
            rtEntity.GetAttributeValueByAccessPath(ckCacheService, fixture.TenantId,
                "RecordArrayTests[0].Designation"));
    }

    [Fact]
    public async Task SetValue_RecordArray_Index_StringValue_NotExisting_Fail()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            {
                "RecordArrayTests", new[]
                {
                    new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                    {
                        { "Designation", "DemoValueInnerString" }
                    })
                }
            }
        };

        var rtEntity = new RtEntity("Test/Country", OctoObjectId.GenerateNewId(), d);

        Assert.Throws<InvalidPathException>(() => RtPathEvaluator.SetValue(ckCacheService, fixture.TenantId, rtEntity,
            "RecordArrayTests[5].InnerString", "new_string"));
    }

    [Fact]
    public async Task SetValue_RecordArray_Last_StringValue_NotExisting_Fail()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            {
                "RecordArrayTests", new[]
                {
                    new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                    {
                        { "Designation", "DemoValueInnerString" }
                    })
                }
            }
        };
        var rtEntity = new RtEntity("Test/Country", OctoObjectId.GenerateNewId(), d);

        Assert.Throws<InvalidPathException>(() => RtPathEvaluator.SetValue(ckCacheService, fixture.TenantId, rtEntity,
            "RecordArrayTests[-1].InnerString", "new_string"));
    }

    [Fact]
    public async Task SetValue_RecordArray_Last_StringValue_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            {
                "RecordArrayTests", new[]
                {
                    new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                    {
                        { "Designation", "DemoValueInnerString1" }
                    }),
                    new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                    {
                        { "Designation", "DemoValueInnerString2" }
                    }),
                    new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                    {
                        { "Designation", "DemoValueInnerString3" }
                    })
                }
            }
        };

        var rtEntity = new RtEntity("Test/Country", OctoObjectId.GenerateNewId(), d);

        RtPathEvaluator.SetValue(ckCacheService, fixture.TenantId, rtEntity,
            "RecordArrayTests[-1].Designation", "new_string");
        var result = RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity,
            "RecordArrayTests[-1].Designation");
        Assert.Equal("new_string", result);
    }

    [Fact]
    public async Task SetValue_RecordArray_Wildcard_StringValue_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            {
                "RecordArrayTests", new[]
                {
                    new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                    {
                        { "Designation", "DemoValueInnerString1" }
                    }),
                    new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                    {
                        { "Designation", "DemoValueInnerString2" }
                    }),
                    new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                    {
                        { "Designation", "DemoValueInnerString3" }
                    })
                }
            }
        };

        var rtEntity = new RtEntity("Test/Country", OctoObjectId.GenerateNewId(), d);

        RtPathEvaluator.SetValue(ckCacheService, fixture.TenantId, rtEntity,
            "RecordArrayTests[*].Designation", "new_string");
        var result = RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity,
            "RecordArrayTests[*].Designation");
        Assert.Equal(["new_string", "new_string", "new_string"], (IEnumerable<object>?)result);
    }

    [Fact]
    public async Task SetValue_IntArray_Wildcard_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" },
            {
                "IntArrayTests", new[]
                {
                    4, 5, 6, 7
                }
            }
        };

        var rtEntity = new RtEntity("Test/Country", OctoObjectId.GenerateNewId(), d);

        RtPathEvaluator.SetValue(ckCacheService, fixture.TenantId, rtEntity, "IntArrayTests[*]", 5);
        var result = RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity, "IntArrayTests[*]");
        Assert.Equal([5, 5, 5, 5], (IEnumerable<object>?)result);
    }

    [Fact]
    public async Task SetValue_Navigation_Record_StringValue_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" }
        };

        var d2 = new Dictionary<string, object?>
        {
            { "Designation", "Child item text" },
            { "DataCount", 49 },
            {
                "RecordTest", new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                {
                    { "Designation", "Navigation-DemoValueInnerString" }
                })
            }
        };

        var associations = new List<NavigationEnd>
        {
            new()
            {
                AssociationId = OctoObjectId.GenerateNewId(),
                AssociationRoleId = new CkId<CkAssociationRoleId>("System/ParentChild"),
                TargetCkTypeId = "Test/Sensor",
                Targets = [new RtEntity("Test/Sensor", OctoObjectId.GenerateNewId(), d2)],
                NavigationPropertyName = "MyNavPropName"
            }
        };

        var rtEntity = new RtEntityGraphItem("Test/Zone", OctoObjectId.GenerateNewId(), d, associations);

        RtPathEvaluator.SetValue(ckCacheService, fixture.TenantId, rtEntity, "myNavPropName.testSensor->recordTest.Designation", "new name of inner string value");
        var result = RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity,
            "myNavPropName.testSensor->recordTest.Designation");
        Assert.Equal("new name of inner string value", result);
    }

    [Fact]
    public async Task SetValue_Navigation_StringValue_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();
        var d = new Dictionary<string, object?>
        {
            { "Designation", "Test" }
        };

        var d2 = new Dictionary<string, object?>
        {
            { "Designation", "Child item text" },
            { "DataCount", 49 },
            {
                "RecordTest", new RtRecord("Test/TestRecord", new Dictionary<string, object?>
                {
                    { "Designation", "Navigation-DemoValueInnerString" }
                })
            }
        };

        var associations = new List<NavigationEnd>
        {
            new()
            {
                AssociationId = OctoObjectId.GenerateNewId(),
                AssociationRoleId = new CkId<CkAssociationRoleId>("System/ParentChild"),
                TargetCkTypeId = "Test/Sensor",
                Targets = [new RtEntity("Test/Sensor", OctoObjectId.GenerateNewId(), d2)],
                NavigationPropertyName = "MyNavPropName"
            }
        };

        var rtEntity = new RtEntityGraphItem("Test/Zone", OctoObjectId.GenerateNewId(), d, associations);

        RtPathEvaluator.SetValue(ckCacheService, fixture.TenantId, rtEntity, "myNavPropName.testSensor->Designation", "new name of string value");
        var result = RtPathEvaluator.GetValue(ckCacheService, fixture.TenantId, rtEntity, "myNavPropName.testSensor->Designation");
        Assert.Equal("new name of string value", result);
    }

    #endregion SetValue

    #region TokenizeAndGetNavigationPairs

    [Fact]
    public async Task TokenizeAndGetNavigationPairs_NoNavigation_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var r = RtPathEvaluator.TokenizeAndGetNavigationPairs(ckCacheService, fixture.TenantId, "Test/Zone",
            "Designation");

        Assert.Null(r);
    }

    [Fact]
    public async Task TokenizeAndGetNavigationPairs_NoNavigation_Record_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var r = RtPathEvaluator.TokenizeAndGetNavigationPairs(ckCacheService, fixture.TenantId, "Test/Zone",
            "RecordTest.Designation");

        Assert.Null(r);
    }

    [Fact]
    public async Task TokenizeAndGetNavigationPairs_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var r = RtPathEvaluator.TokenizeAndGetNavigationPairs(ckCacheService, fixture.TenantId, "Test/Zone",
            "children.testLocationWithSensor->Designation");

        Assert.NotNull(r);
        Assert.Equal("System/ParentChild", r.CkRoleId);
        Assert.Equal("Test/LocationWithSensor", r.TargetCkTypeId);
        Assert.Equal(GraphDirections.Inbound, r.Direction);
    }


    [Fact]
    public async Task TokenizeAndGetNavigationPairs_InheritedTargetType_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var r = RtPathEvaluator.TokenizeAndGetNavigationPairs(ckCacheService, fixture.TenantId, "Test/Sensor",
            "parent.testZone->Designation");

        Assert.NotNull(r);
        Assert.Equal("System/ParentChild", r.CkRoleId);
        Assert.Equal("Test/Zone", r.TargetCkTypeId);
        Assert.Equal(GraphDirections.Outbound, r.Direction);
    }

    [Fact]
    public async Task TokenizeAndGetNavigationPairs_Two_Layers_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var r = RtPathEvaluator.TokenizeAndGetNavigationPairs(ckCacheService, fixture.TenantId, "Test/Sensor",
            "parent.testZone->parent.testDistrict->Designation");

        Assert.NotNull(r);
        Assert.Equal("System/ParentChild", r.CkRoleId);
        Assert.Equal("Test/Zone", r.TargetCkTypeId);
        Assert.Equal(GraphDirections.Outbound, r.Direction);
        Assert.NotNull(r.InnerNavigationPair);
        Assert.Equal("System/ParentChild", r.InnerNavigationPair.CkRoleId);
        Assert.Equal("Test/District", r.InnerNavigationPair.TargetCkTypeId);
        Assert.Equal(GraphDirections.Outbound, r.InnerNavigationPair.Direction);
    }

    [Fact]
    public async Task TokenizeAndGetNavigationPairs_Three_Layers_OK()
    {
        var ckCacheService = await fixture.GetCacheServiceAsync();

        var r = RtPathEvaluator.TokenizeAndGetNavigationPairs(ckCacheService, fixture.TenantId, "Test/Sensor",
            "parent.testZone->parent.testDistrict->parent.testCity->Designation");

        Assert.NotNull(r);
        Assert.Equal("System/ParentChild", r.CkRoleId);
        Assert.Equal("Test/Zone", r.TargetCkTypeId);
        Assert.Equal(GraphDirections.Outbound, r.Direction);
        Assert.NotNull(r.InnerNavigationPair);
        Assert.Equal("System/ParentChild", r.InnerNavigationPair.CkRoleId);
        Assert.Equal("Test/District", r.InnerNavigationPair.TargetCkTypeId);
        Assert.Equal(GraphDirections.Outbound, r.InnerNavigationPair.Direction);
        Assert.NotNull(r.InnerNavigationPair.InnerNavigationPair);
        Assert.Equal("System/ParentChild", r.InnerNavigationPair.InnerNavigationPair.CkRoleId);
        Assert.Equal("Test/City", r.InnerNavigationPair.InnerNavigationPair.TargetCkTypeId);
        Assert.Equal(GraphDirections.Outbound, r.InnerNavigationPair.InnerNavigationPair.Direction);
    }

    #endregion TokenizeAndGetNavigationPairs
}
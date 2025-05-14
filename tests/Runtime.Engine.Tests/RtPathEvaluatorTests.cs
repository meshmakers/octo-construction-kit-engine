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

    #endregion TokenizePath

    #region GetValue

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
}
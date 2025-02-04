using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts;

namespace Meshmakers.Octo.Runtime.Engine.Tests;

public class RtPathEvaluatorTests
{
    [Fact]
    public void SimpleStringValue_OK()
    {
        var d = new Dictionary<string, object?>
        {
            { "Name", "Test" },
            { "Enabled", true }
        };

        var rtEntity = new RtEntity("Demo/Test", OctoObjectId.GenerateNewId(), d);

        var result = RtPathEvaluator.GetValue(rtEntity, "Name");
        Assert.Equal("Test", result);
    }

    [Fact]
    public void SimpleBoolValue_OK()
    {
        var d = new Dictionary<string, object?>
        {
            { "Name", "Test" },
            { "Enabled", true }
        };

        var rtEntity = new RtEntity("Demo/Test", OctoObjectId.GenerateNewId(), d);

        var result = RtPathEvaluator.GetValue(rtEntity, "Enabled");
        Assert.Equal(true, result);
    }

    [Fact]
    public void EmbeddedDocument_Record_StringValue_OK()
    {
        var d = new Dictionary<string, object?>
        {
            { "Name", "Test" },
            { "Enabled", true },
            { "Value", 42 },
            {
                "Record", new RtRecord("Demo/Record1", new Dictionary<string, object?>
                {
                    { "InnerString", "DemoValueInnerString" }
                })
            }
        };

        var rtEntity = new RtEntity("Demo/Test", OctoObjectId.GenerateNewId(), d);

        var result = RtPathEvaluator.GetValue(rtEntity, "Record.InnerString");
        Assert.Equal("DemoValueInnerString", result);
    }

    [Fact]
    public void EmbeddedDocument_RecordArray_Index_StringValue_OK()
    {
        var d = new Dictionary<string, object?>
        {
            { "Name", "Test" },
            { "Enabled", true },
            { "Value", 42 },
            {
                "Records", new[]
                {
                    new RtRecord("Demo/Record1", new Dictionary<string, object?>
                    {
                        { "InnerString", "DemoValueInnerString" }
                    })
                }
            }
        };

        var rtEntity = new RtEntity("Demo/Test", OctoObjectId.GenerateNewId(), d);

        var result = RtPathEvaluator.GetValue(rtEntity, "Records[0].InnerString");
        Assert.Equal("DemoValueInnerString", result);
    }

    [Fact]
    public void EmbeddedDocument_RecordArray_Index_StringValue_NotExisting_OK()
    {
        var d = new Dictionary<string, object?>
        {
            { "Name", "Test" },
            { "Enabled", true },
            { "Value", 42 },
            {
                "Records", new[]
                {
                    new RtRecord("Demo/Record1", new Dictionary<string, object?>
                    {
                        { "InnerString", "DemoValueInnerString" }
                    })
                }
            }
        };

        var rtEntity = new RtEntity("Demo/Test", OctoObjectId.GenerateNewId(), d);

        var result = RtPathEvaluator.GetValue(rtEntity, "Records[5].InnerString");
        Assert.Null(result);
    }

    [Fact]
    public void EmbeddedDocument_RecordArray_Last_StringValue_NotExisting_OK()
    {
        var d = new Dictionary<string, object?>
        {
            { "Name", "Test" },
            { "Enabled", true },
            { "Value", 42 },
            {
                "Records", new Dictionary<string, object>()

            }
        };

        var rtEntity = new RtEntity("Demo/Test", OctoObjectId.GenerateNewId(), d);

        var result = RtPathEvaluator.GetValue(rtEntity, "Records[-1].InnerString");
        Assert.Null(result);
    }

    [Fact]
    public void EmbeddedDocument_RecordArray_Last_StringValue_OK()
    {
        var d = new Dictionary<string, object?>
        {
            { "Name", "Test" },
            { "Enabled", true },
            { "Value", 42 },
            {
                "Records", new[]
                {
                    new RtRecord("Demo/Record1", new Dictionary<string, object?>
                    {
                        { "InnerString", "DemoValueInnerString0" }
                    }),
                    new RtRecord("Demo/Record1", new Dictionary<string, object?>
                    {
                        { "InnerString", "DemoValueInnerString1" }
                    }),
                    new RtRecord("Demo/Record1", new Dictionary<string, object?>
                    {
                        { "InnerString", "DemoValueInnerString2" }
                    })
                }
            }
        };

        var rtEntity = new RtEntity("Demo/Test", OctoObjectId.GenerateNewId(), d);

        var result = RtPathEvaluator.GetValue(rtEntity, "Records[-1].InnerString");
        Assert.Equal("DemoValueInnerString2", result);
    }

    [Fact]
    public void EmbeddedDocument_RecordArray_Wildcard_StringValue_OK()
    {
        var d = new Dictionary<string, object?>
        {
            { "Name", "Test" },
            { "Enabled", true },
            { "Value", 42 },
            {
                "Records", new[]
                {
                    new RtRecord("Demo/Record1", new Dictionary<string, object?>
                    {
                        { "InnerString", "DemoValueInnerString0" }
                    }),
                    new RtRecord("Demo/Record1", new Dictionary<string, object?>
                    {
                        { "InnerString", "DemoValueInnerString1" }
                    }),
                    new RtRecord("Demo/Record1", new Dictionary<string, object?>
                    {
                        { "InnerString", "DemoValueInnerString2" }
                    })
                }
            }
        };

        var rtEntity = new RtEntity("Demo/Test", OctoObjectId.GenerateNewId(), d);

        var result = RtPathEvaluator.GetValue(rtEntity, "Records[*].InnerString");
        Assert.Equal(["DemoValueInnerString0", "DemoValueInnerString1", "DemoValueInnerString2"], (IEnumerable<object>?)result);
    }

    [Fact]
    public void EmbeddedDocument_IntArray_Wildcard_OK()
    {
        var d = new Dictionary<string, object?>
        {
            { "Name", "Test" },
            { "Enabled", true },
            { "Value", new[]
                {
                    4, 5, 6, 7
                }
            }
        };

        var rtEntity = new RtEntity("Demo/Test", OctoObjectId.GenerateNewId(), d);

        var result = RtPathEvaluator.GetValue(rtEntity, "Value[*]");
        Assert.Equal([4, 5, 6, 7 ], (IEnumerable<object>?)result);
    }
}
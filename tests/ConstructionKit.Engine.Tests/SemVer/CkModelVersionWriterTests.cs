using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.SemVer;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.SemVer;

/// <summary>
///     AB#4467: the version rewrite of <c>ckModel.yaml</c> changes the version text and nothing else.
/// </summary>
public class CkModelVersionWriterTests
{
    private static readonly CkVersion Three = new(3, 0, 0);

    [Theory]
    [InlineData("modelId: Basic-2.5.0\n", "modelId: Basic-3.0.0\n")]
    [InlineData("modelId: \"Basic-2.5.0\"\n", "modelId: \"Basic-3.0.0\"\n")]
    [InlineData("modelId: 'Basic-2.5.0'   # the version\n", "modelId: 'Basic-3.0.0'   # the version\n")]
    [InlineData("modelId:    Basic-2.5\n", "modelId:    Basic-3.0.0\n")]
    [InlineData("\"modelId\": Basic-2.5.0", "\"modelId\": Basic-3.0.0")]
    public void Only_the_version_text_changes(string input, string expected)
    {
        Assert.True(CkModelVersionWriter.TryReplaceVersion(input, "Basic", Three, out var updated));
        Assert.Equal(expected, updated);
    }

    [Fact]
    public void Comments_key_order_and_crlf_line_endings_are_preserved()
    {
        const string yaml = "# header\r\n\"$schema\": \"https://x\"\r\ndependencies:\r\n  - System-[2.4,3.0)\r\nmodelId: Basic-2.5.0\r\n# trailing\r\n";

        Assert.True(CkModelVersionWriter.TryReplaceVersion(yaml, "Basic", Three, out var updated));

        Assert.Equal(yaml.Replace("Basic-2.5.0", "Basic-3.0.0"), updated);
    }

    [Fact]
    public void A_dependency_with_the_same_name_pattern_is_not_touched()
    {
        const string yaml = "dependencies:\n  - Basic-[2.0,3.0)\nmodelId: Basic-2.5.0\n";

        Assert.True(CkModelVersionWriter.TryReplaceVersion(yaml, "Basic", Three, out var updated));

        Assert.Equal("dependencies:\n  - Basic-[2.0,3.0)\nmodelId: Basic-3.0.0\n", updated);
    }

    [Theory]
    [InlineData("modelId: Other-2.5.0\n")]
    [InlineData("modelId: Basic-2.5.0\nmodelId: Basic-2.6.0\n")]
    [InlineData("description: no model id\n")]
    [InlineData("modelId: Basic-${version}\n")]
    public void Nothing_is_written_unless_there_is_exactly_one_matching_line(string input)
    {
        Assert.False(CkModelVersionWriter.TryReplaceVersion(input, "Basic", Three, out var updated));
        Assert.Equal(input, updated);
    }

    [Fact]
    public void A_look_alike_line_inside_a_block_scalar_is_never_touched()
    {
        const string yaml = "\"modelId\": Basic-2.5.0\ndescription: |\n  modelId: Basic-2.5.0\n";

        Assert.True(CkModelVersionWriter.TryReplaceVersion(yaml, "Basic", Three, out var updated));

        Assert.Equal("\"modelId\": Basic-3.0.0\ndescription: |\n  modelId: Basic-2.5.0\n", updated);
    }

    [Fact]
    public void Only_an_indented_look_alike_means_there_is_nothing_to_rewrite()
    {
        const string yaml = "description: |\n  modelId: Basic-2.5.0\n";

        Assert.False(CkModelVersionWriter.TryReplaceVersion(yaml, "Basic", Three, out _));
    }
}

using System.Reflection;
using System.Text.Json;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Tests.Messages;

/// <summary>
///     F1.1-S1 (AB#5904): <c>MessageCodes.cs</c> is generated from <c>MessageCodes.json</c> by a T4 template that is
///     not part of the build, so the two drifted before (codes 67-69). Both tables must be identical — key, number,
///     level and text — and every number must be unique.
/// </summary>
public class MessageCodesSyncTests
{
    private sealed record JsonMessage(string Key, int Number, string Level, string Text);

    private static IReadOnlyList<JsonMessage> ReadJson()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(directory.FullName, "src", "ConstructionKit.Engine", "Messages",
                   "MessageCodes.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory!.FullName, "src",
            "ConstructionKit.Engine", "Messages", "MessageCodes.json")));
        return document.RootElement.GetProperty("operationMessages").EnumerateArray()
            .Select(e => new JsonMessage(e.GetProperty("key").GetString()!, e.GetProperty("number").GetInt32(),
                e.GetProperty("level").GetString()!, e.GetProperty("text").GetString()!))
            .ToList();
    }

    private static IReadOnlyDictionary<string, OperationMessageTemplate> ReadCSharp()
    {
        var field = typeof(MessageCodes).GetField("Templates", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return (IReadOnlyDictionary<string, OperationMessageTemplate>)field!.GetValue(null)!;
    }

    private static MessageLevel ParseLevel(string level) => level switch
    {
        "INFO" => MessageLevel.Info,
        "WARNING" => MessageLevel.Warning,
        "ERROR" => MessageLevel.Error,
        "FATALERROR" => MessageLevel.FatalError,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown level in MessageCodes.json")
    };

    [Fact]
    public void JsonAndCSharpTables_AreIdentical()
    {
        var json = ReadJson();
        var csharp = ReadCSharp();

        Assert.Equal(json.Select(m => m.Key).OrderBy(k => k, StringComparer.Ordinal),
            csharp.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var message in json)
        {
            var template = csharp[message.Key];
            Assert.True(message.Number == template.MessageNumber,
                $"{message.Key}: number {message.Number} (json) vs {template.MessageNumber} (C#)");
            Assert.True(ParseLevel(message.Level) == template.MessageLevel,
                $"{message.Key}: level {message.Level} (json) vs {template.MessageLevel} (C#)");
            Assert.True(message.Text == template.MessageText,
                $"{message.Key}: text differs between MessageCodes.json and MessageCodes.cs");
        }
    }

    [Fact]
    public void MessageNumbers_AreUnique()
    {
        var duplicates = ReadJson().GroupBy(m => m.Number).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(m => m.Key))}")
            .ToList();

        Assert.True(duplicates.Count == 0, "Duplicate message numbers: " + string.Join("; ", duplicates));
    }
}

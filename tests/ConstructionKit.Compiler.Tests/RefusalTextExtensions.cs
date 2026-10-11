using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Tests;

internal static class RefusalTextExtensions
{
    /// <summary>The summary followed by the reason lines of a refusal, as <c>ckc</c> prints them.</summary>
    public static string AllText(this CkPublishRefusedException exception) =>
        exception.Message + "\n" + string.Join("\n", exception.OperationResult.Messages.Select(m => m.MessageText));
}

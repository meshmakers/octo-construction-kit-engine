using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers.RangeRetention;

/// <summary>
///     Deep copy of a compiled model through its JSON catalog form — the canonical serialization, so no
///     property can be forgotten by a hand-written copy. Used where references must be rewritten without
///     touching the caller's instance (the compiled output, or the model about to be persisted).
/// </summary>
internal static class CkCompiledModelCloner
{
    public static async Task<CkCompiledModelRoot> CloneAsync(ICkJsonSerializer serializer, CkCompiledModelRoot model)
    {
        using var memoryStream = new MemoryStream();
#if NETSTANDARD2_0
        using (var writer = new StreamWriter(memoryStream, new UTF8Encoding(false), 4096, true))
#else
        await using (var writer = new StreamWriter(memoryStream, new UTF8Encoding(false), 4096, true))
#endif
        {
            await serializer.SerializeAsync(writer, model).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }

        var json = Encoding.UTF8.GetString(memoryStream.ToArray());
        var operationResult = new OperationResult();
        var clone = await serializer.DeserializeCompiledModelRootAsync(json, $"clone of {model.ModelId}",
            operationResult, tolerantToUnknownProperties: true).ConfigureAwait(false);
        if (operationResult.HasErrors || operationResult.HasFatalErrors)
        {
            throw new InvalidOperationException(
                $"Could not copy CK model '{model.ModelId}': {string.Join("; ", operationResult.Messages)}");
        }

        return clone;
    }
}

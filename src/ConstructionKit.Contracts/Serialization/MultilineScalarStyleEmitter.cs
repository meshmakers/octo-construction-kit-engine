using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.EventEmitters;

namespace Meshmakers.Octo.ConstructionKit.Contracts.Serialization;

/// <summary>
/// Ensures multi-line strings serialize using literal block scalar style (|) for consistent
/// and readable YAML output. Without this emitter, YamlDotNet may inconsistently choose
/// between folded (>), literal (|), or quoted string styles based on internal heuristics.
/// </summary>
public sealed class MultilineScalarStyleEmitter : ChainedEventEmitter
{
    // All Unicode newline characters: CR, LF, NEL (Next Line), LS (Line Separator), PS (Paragraph Separator)
    private static readonly char[] NewlineCharacters = { '\r', '\n', '\x85', '\u2028', '\u2029' };

    /// <summary>
    /// Creates a new instance of the <see cref="MultilineScalarStyleEmitter"/> class.
    /// </summary>
    /// <param name="nextEmitter">The next emitter in the chain.</param>
    public MultilineScalarStyleEmitter(IEventEmitter nextEmitter) : base(nextEmitter)
    {
    }

    /// <inheritdoc />
    public override void Emit(ScalarEventInfo eventInfo, IEmitter emitter)
    {
        if (typeof(string).IsAssignableFrom(eventInfo.Source.Type))
        {
            var value = eventInfo.Source.Value as string;
            if (value != null && value.IndexOfAny(NewlineCharacters) >= 0)
            {
                eventInfo = new ScalarEventInfo(eventInfo.Source) { Style = ScalarStyle.Literal };
            }
        }

        base.Emit(eventInfo, emitter);
    }
}

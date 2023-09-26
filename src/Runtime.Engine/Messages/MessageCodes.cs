
//------------------------------------------------------------------------------
// <auto-generate>
//     The code was generated from a template.
//
//     Modifications to this file may result in incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Messages;

/// <summary>
/// Defines possible messages for:
/// Information
/// Warnings
/// Errors
/// </summary>
[System.Diagnostics.DebuggerNonUserCodeAttribute()]
[System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
internal static class MessageCodes
{
    // ReSharper disable once MemberCanBePrivate.Global
    internal static CompilerMessage GetMessage(string messageKey, params object[] args)
    {
        if (!Templates.ContainsKey(messageKey))
        {
            throw new ArgumentOutOfRangeException($"Message with key '{messageKey}' does not exist.");
        }
        return Templates[messageKey].CreateMessage(args);
    }

    internal static CompilerMessage SchemaValidationError(object locationReference, object errorMessage) =>
        GetMessage("SchemaValidationError", locationReference, errorMessage);

    private static readonly Dictionary<string, CompilerMessageTemplate> Templates = new()
    {
        {
            "SchemaValidationError",
             new CompilerMessageTemplate(MessageLevel.Error,
                 1, "{locationReference}: Schema validation failed: '{errorMessage}'",
                 new [] {"locationReference", "errorMessage"})
        },
    };
}


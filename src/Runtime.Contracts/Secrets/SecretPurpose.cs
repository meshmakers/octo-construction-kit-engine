namespace Meshmakers.Octo.Runtime.Contracts.Secrets;

/// <summary>
///     What a value protected outside a Secret attribute is for, e.g. a sealed value of one execution of one tenant.
///     It is authenticated together with the envelope header, so the value opens only for exactly this purpose and
///     never as the value of a Secret attribute (and a Secret attribute value never opens for a purpose).
/// </summary>
/// <param name="Value">
///     Non-empty purpose text. Callers include everything the value is bound to (for example
///     <c>queued-caller/&lt;tenant&gt;/&lt;execution&gt;</c>); the text itself is not secret and is not stored.
/// </param>
public readonly record struct SecretPurpose(string Value);

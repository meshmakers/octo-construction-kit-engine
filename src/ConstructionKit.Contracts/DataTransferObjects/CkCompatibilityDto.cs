using YamlDotNet.Serialization;

namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     The optional <c>compatibility</c> section of <c>ckModel.yaml</c> (AB#6295): decisions of the model author about
///     the compatibility gates. Carried into the compiled model JSON so the publish gate can enforce the same rule
///     without the source. Not part of the model structure and therefore not diffed.
/// </summary>
public class CkCompatibilityDto
{
    /// <summary>
    ///     Explicit acknowledgements of changes that need one (a security-sensitive access tightening, a unique index
    ///     on a stable base). Valid for exactly the release they are in: an entry that matches no change of the release
    ///     is stale and fails the build (<c>OCTO-CK204</c>).
    /// </summary>
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public List<CkAcknowledgeDto>? Acknowledge { get; set; }
}

/// <summary>
///     One acknowledged change: the change key printed by the gate and the mandatory reason (AB#6295).
/// </summary>
public class CkAcknowledgeDto
{
    /// <summary>
    ///     The change key exactly as the gate prints it (<c>CkChangeKey</c>): stable, free of version numbers,
    ///     no wildcards.
    /// </summary>
    public string Change { get; set; } = "";

    /// <summary>
    ///     Why the change is accepted (for the audit trail in the verdict and the CHANGELOG). Must not be empty.
    /// </summary>
    public string Reason { get; set; } = "";
}

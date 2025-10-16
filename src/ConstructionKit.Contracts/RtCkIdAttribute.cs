namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Defines the construction kit type id
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class RtCkIdAttribute : Attribute
{
    /// <summary>
    ///     Creates a new instance of <see cref="RtCkIdAttribute" />
    /// </summary>
    /// <param name="modelId"></param>
    /// <param name="key"></param>
    public RtCkIdAttribute(string modelId, string key)
    {
        RtCkId = new RtCkId<CkTypeId>(modelId, key);
    }

    /// <summary>
    ///     Creates a new instance of <see cref="RtCkIdAttribute" />
    /// </summary>
    /// <param name="ckId"></param>
    public RtCkIdAttribute(string ckId)
    {
        RtCkId = new RtCkId<CkTypeId>(ckId);
    }

    /// <summary>
    ///     Returns the construction kit type id
    /// </summary>
    public RtCkId<CkTypeId> RtCkId { get; }
}
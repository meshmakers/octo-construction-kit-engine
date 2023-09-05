namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Defines the construction kit type id
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CkIdAttribute : Attribute
{
    /// <summary>
    /// Creates a new instance of <see cref="CkIdAttribute"/>
    /// </summary>
    /// <param name="modelId"></param>
    /// <param name="key"></param>
    public CkIdAttribute(string modelId, string key)
    {
        CkId = new CkId<CkTypeId>(modelId, key);
    }
    
    /// <summary>
    /// Creates a new instance of <see cref="CkIdAttribute"/>
    /// </summary>
    /// <param name="ckId"></param>
    public CkIdAttribute(string ckId)
    {
        CkId = new CkId<CkTypeId>(ckId);
    }

    /// <summary>
    /// Returns the construction kit type id
    /// </summary>
    public CkId<CkTypeId> CkId { get; }
}

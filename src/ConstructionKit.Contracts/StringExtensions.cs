namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Extensions to string
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Makes from a construction kit id a valid C# class name
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    public static string MakeClassName(this string s)
    {
        return s.Trim().Replace(".", "");
    }
}
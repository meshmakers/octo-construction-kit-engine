namespace Meshmakers.Octo.ConstructionKit.MsBuildTasks;

internal static class PathExtensions
{
    public static string GetOsSpecificPath(this string path)
    {
        var r = path.Replace('/', Path.DirectorySeparatorChar);
        return r.Replace('\\', Path.DirectorySeparatorChar);
    }
}
namespace CkModel.Compiler.SystemTests.Fixtures;

public class TemporaryDirectoryFixture : ServiceCollectionFixture, IDisposable
{
    private readonly List<string> _tempDirectoryList;
    public string CreateTempDirectory()
    {
        var tempDirectoryPath = Path.Combine(Path.GetTempPath(), "CkModelCompilerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectoryPath);
        _tempDirectoryList.Add(tempDirectoryPath);
        return tempDirectoryPath;
    }

    public TemporaryDirectoryFixture()
    {
        _tempDirectoryList = new List<string>();

    }

    public void Dispose()
    {
        foreach (var tempDirectoryPath in _tempDirectoryList)
        {
            Directory.Delete(tempDirectoryPath, true);

        }
    }
}
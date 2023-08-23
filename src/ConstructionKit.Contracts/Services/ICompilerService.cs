namespace Meshmakers.Octo.ConstructionKit.Contracts.Services;

public interface ICompilerService
{
    Task CreateNewAsync(string rootPath);
    Task CompileAsync(string rootPath);
}
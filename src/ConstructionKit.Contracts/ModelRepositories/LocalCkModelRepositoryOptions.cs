namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;

public class LocalCkModelRepositoryOptions
{
    public LocalCkModelRepositoryOptions()
    {
        RootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            $".octo-ck-models");
    }
    
    public string RootPath { get; set; } = null!;
}
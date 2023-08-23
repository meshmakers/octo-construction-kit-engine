namespace Meshmakers.Octo.ConstructionKit.Contracts;

public class ModelRepositoryException : Exception
{
    public ModelRepositoryException()
    {
    }

    public ModelRepositoryException(string message) : base(message)
    {
    }

    public ModelRepositoryException(string message, Exception inner) : base(message, inner)
    {
    }

    public static Exception ModelNotFoundInRepositories(CkModelId ckModelId)
    {
        return new ModelRepositoryException($"Model '{ckModelId}' not found in one of the defined model repositories.");
    }
    
    public static Exception ModelNotFound(CkModelId ckModelId, string repositoryName)
    {
        return new ModelRepositoryException($"Model '{ckModelId}' not found in repository '{repositoryName}'.");
    }

    public static Exception ErrorDuringModelLoad(CkModelId ckModelId, string repositoryName, OperationResult operationResult)
    {
        return new ModelRepositoryException($"Error loading model '{ckModelId}' from repository '{repositoryName}'.{Environment.NewLine}{operationResult.GetMessages()}");
    }
}

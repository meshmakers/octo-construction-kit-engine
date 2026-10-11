namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     The publish gate refused a model (AB#6386, AB#6389): nothing was written to the catalog. It is a verdict about
///     the model, like <c>ValidateVersion</c>, <c>ValidateCascade</c> and <c>ValidateCatalog</c>, so it ends the process
///     with the same code (<c>-6</c>, <see cref="ModelValidationException" />). The reasons (ordered, deduplicated, one
///     self-contained line each, see <c>CkPublishRefusal</c>) are in <see cref="OperationResult" />; the exception text
///     is the one-line summary.
/// </summary>
public sealed class CkPublishRefusedException : ModelValidationException
{
    /// <summary>
    ///     Creates a new instance of the <see cref="CkPublishRefusedException" /> class.
    /// </summary>
    /// <param name="message">The one-line summary (model, catalog, gate, codes).</param>
    /// <param name="operationResult">The result that carries one error message per reason.</param>
    public CkPublishRefusedException(string message, OperationResult operationResult) : base(message)
    {
        OperationResult = operationResult;
    }

    /// <summary>The result that carries one error message per reason.</summary>
    public OperationResult OperationResult { get; }
}

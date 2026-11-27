using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Messages;
using Meshmakers.Octo.Runtime.Contracts.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Tests.Repositories;

/// <summary>
///     The stable error contract of the released-document refusals. Services answer these codes (GraphQL
///     <c>extensions.code</c>, the pipeline node's error), and a job status that carries only the exception message (the
///     asynchronous RT import) carries the code because the message starts with it. The literals are pinned here, so a
///     rename breaks this test instead of every client.
/// </summary>
public class LockRefusalErrorCodeTests
{
    private static OperationResult OneError(int number)
    {
        var result = new OperationResult();
        result.AddMessage(new OperationMessage(MessageLevel.Error, "Invoice@1", number, "refused"));
        return result;
    }

    [Fact]
    public void ReleasedDocumentLocked_HasTheStableCode_AndItsMessageStartsWithIt()
    {
        Assert.Equal("RELEASED_DOCUMENT_LOCKED", ReleasedDocumentLockedException.ErrorCode);
        Assert.Equal(168, ReleasedDocumentLockedException.MessageNumber);

        var exception = new ReleasedDocumentLockedException(OneError(168), []);

        Assert.StartsWith("RELEASED_DOCUMENT_LOCKED: ", exception.Message, StringComparison.Ordinal);
        Assert.Contains(" 168 ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StornoRefused_HasTheStableCode_AndItsMessageStartsWithIt()
    {
        Assert.Equal("STORNO_REFUSED", StornoRefusedException.ErrorCode);

        var exception = new StornoRefusedException(OneError(170));

        Assert.StartsWith("STORNO_REFUSED: ", exception.Message, StringComparison.Ordinal);
        Assert.Contains(" 170 ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentAuditEntryAppendOnly_HasTheStableCode_AndItsMessageStartsWithIt()
    {
        Assert.Equal("DOCUMENT_AUDIT_ENTRY_APPEND_ONLY", DocumentAuditEntryAppendOnlyException.ErrorCode);
        Assert.Equal(173, DocumentAuditEntryAppendOnlyException.MessageNumber);

        var exception = new DocumentAuditEntryAppendOnlyException(OneError(173));

        Assert.StartsWith("DOCUMENT_AUDIT_ENTRY_APPEND_ONLY: ", exception.Message, StringComparison.Ordinal);
        Assert.Contains(" 173 ", exception.Message, StringComparison.Ordinal);
    }
}

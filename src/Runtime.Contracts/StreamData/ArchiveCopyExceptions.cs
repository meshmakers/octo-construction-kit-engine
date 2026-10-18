#pragma warning disable CS1591 // Missing XML comments on stream data contracts
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// The repository does not implement the native copy (<see cref="IStreamDataRepository.SupportsStagingCopy"/> is
/// <c>false</c>). The caller takes the row path (dump format 1) instead.
/// </summary>
public sealed class ArchiveCopyNotSupportedException : StreamDataException
{
    public ArchiveCopyNotSupportedException(OctoObjectId? archiveRtId = null)
        : base("This stream data repository does not support the native copy to a staging location " +
               "(IStreamDataRepository.SupportsStagingCopy is false). Use the row export and import instead.",
            archiveRtId)
    {
    }
}

/// <summary>Why a native copy was refused before it moved any data.</summary>
public enum ArchiveCopyRefusalReason
{
    /// <summary>
    /// <c>stats.jobs_log_filter</c> does not keep <c>COPY</c> statements out of <c>sys.jobs_log</c>, so the
    /// credential in the statement would stay readable there.
    /// </summary>
    JobLogNotFiltered = 0,

    /// <summary>The dumped table has a column that the table to load into lacks.</summary>
    ColumnMissing = 1,

    /// <summary>A node does not have the free disk space the load needs.</summary>
    InsufficientDiskSpace = 2
}

/// <summary>
/// A native copy was refused before it moved any data. The message names what is wrong (the setting, the
/// column, the expected and the available size) and never contains the staging credential.
/// </summary>
public sealed class ArchiveCopyRefusedException : StreamDataException
{
    public ArchiveCopyRefusedException(ArchiveCopyRefusalReason reason, string message, OctoObjectId? archiveRtId = null)
        : base(message, archiveRtId)
    {
        Reason = reason;
    }

    public ArchiveCopyRefusalReason Reason { get; }
}

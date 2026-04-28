#pragma warning disable CS1591 // Missing XML docs on archive exception members
using System;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.StreamData;

/// <summary>
/// Base exception for stream-data and archive failures (concept §12). Carries the offending
/// archive id where relevant.
/// </summary>
public class StreamDataException : Exception
{
    public OctoObjectId? ArchiveRtId { get; }

    public StreamDataException(string message, OctoObjectId? archiveRtId = null, Exception? inner = null)
        : base(message, inner)
    {
        ArchiveRtId = archiveRtId;
    }
}

/// <summary>
/// The archive identified by <see cref="StreamDataException.ArchiveRtId"/> does not exist.
/// </summary>
public sealed class ArchiveNotFoundException : StreamDataException
{
    public ArchiveNotFoundException(OctoObjectId archiveRtId)
        : base($"Archive '{archiveRtId}' does not exist.", archiveRtId) { }
}

/// <summary>
/// The archive exists but its current <see cref="CkArchiveStatus"/> does not allow the requested
/// data-plane operation (insert / query). Carries the archive's actual status.
/// </summary>
public sealed class ArchiveNotActivatedException : StreamDataException
{
    public CkArchiveStatus ActualStatus { get; }

    public ArchiveNotActivatedException(OctoObjectId archiveRtId, CkArchiveStatus actualStatus)
        : base($"Archive '{archiveRtId}' is in status {actualStatus} and does not accept inserts or queries.", archiveRtId)
    {
        ActualStatus = actualStatus;
    }
}

/// <summary>
/// A lifecycle transition was requested but the archive's current status does not allow it.
/// </summary>
public sealed class InvalidArchiveStateTransitionException : StreamDataException
{
    public CkArchiveStatus FromStatus { get; }
    public string AttemptedTransition { get; }

    public InvalidArchiveStateTransitionException(
        OctoObjectId archiveRtId, CkArchiveStatus from, string attempted)
        : base($"Cannot {attempted} archive '{archiveRtId}' from status {from}.", archiveRtId)
    {
        FromStatus = from;
        AttemptedTransition = attempted;
    }
}

/// <summary>
/// A schema-relevant change (target type or columns) was attempted on an archive that has already
/// left the <see cref="CkArchiveStatus.Created"/> state.
/// </summary>
public sealed class ArchiveSchemaImmutableException : StreamDataException
{
    public ArchiveSchemaImmutableException(OctoObjectId archiveRtId, CkArchiveStatus currentStatus)
        : base($"Archive '{archiveRtId}' is in status {currentStatus}; its column definitions and target type are frozen.", archiveRtId) { }
}

/// <summary>
/// One of the archive's <c>CkArchiveColumn.Path</c> entries does not resolve against the current
/// CK model (unknown attribute, broken record traversal, illegal array indexing).
/// </summary>
public sealed class ArchivePathInvalidException : StreamDataException
{
    public string Path { get; }

    public ArchivePathInvalidException(OctoObjectId archiveRtId, string path, string message)
        : base($"Archive '{archiveRtId}' path '{path}' is invalid: {message}", archiveRtId)
    {
        Path = path;
    }
}

/// <summary>
/// Raised when activation DDL fails during <see cref="CkArchiveStatus.Created"/> /
/// <see cref="CkArchiveStatus.Failed"/> → <see cref="CkArchiveStatus.Activated"/>. Wraps the
/// underlying SQL error.
/// </summary>
public sealed class ArchiveActivationFailedException : StreamDataException
{
    public ArchiveActivationFailedException(OctoObjectId archiveRtId, Exception inner)
        : base($"Activation of archive '{archiveRtId}' failed: {inner.Message}", archiveRtId, inner) { }
}

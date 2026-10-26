using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.CustomMessage;

// Snapshots of the machine for this session, see SnapshotManager. Taking one works while running, restoring needs the
// emulator to be stopped and sends a stopped event so VSCode refreshes.

internal class TakeSnapshotRequest : DebugRequestWithResponse<TakeSnapshotRequestArguments, SnapshotRequestResponse>
{
    public TakeSnapshotRequest() : base("takeSnapshot")
    {
    }
}

internal class GetSnapshotsRequest : DebugRequestWithResponse<GetSnapshotsRequestArguments, GetSnapshotsRequestResponse>
{
    public GetSnapshotsRequest() : base("getSnapshots")
    {
    }
}

internal class RestoreSnapshotRequest : DebugRequestWithResponse<SnapshotIdRequestArguments, SnapshotRequestResponse>
{
    public RestoreSnapshotRequest() : base("restoreSnapshot")
    {
    }
}

internal class DeleteSnapshotRequest : DebugRequestWithResponse<SnapshotIdRequestArguments, SnapshotRequestResponse>
{
    public DeleteSnapshotRequest() : base("deleteSnapshot")
    {
    }
}

internal class RenameSnapshotRequest : DebugRequestWithResponse<RenameSnapshotRequestArguments, SnapshotRequestResponse>
{
    public RenameSnapshotRequest() : base("renameSnapshot")
    {
    }
}

public class TakeSnapshotRequestArguments : DebugRequestArguments
{
}

public class GetSnapshotsRequestArguments : DebugRequestArguments
{
}

public class SnapshotIdRequestArguments : DebugRequestArguments
{
    public int Id { get; set; }
}

public class RenameSnapshotRequestArguments : DebugRequestArguments
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class SnapshotRequestResponse : ResponseBody
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public SnapshotInfo? Snapshot { get; set; }

    // restore only: the modem is running, and it isn't part of the snapshot
    public bool ModemActive { get; set; }

    // restore only: it failed because the emulator is running, pause and try again
    public bool Running { get; set; }
}

public class GetSnapshotsRequestResponse : ResponseBody
{
    public List<SnapshotInfo> Snapshots { get; set; } = new();
}

public class SnapshotInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Created { get; set; } = "";
    public uint Frame { get; set; }
    public int BeamX { get; set; }
    public int BeamY { get; set; }
    public string Thumbnail { get; set; } = ""; // png, base64
}

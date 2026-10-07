using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.CustomMessage;

internal class StartAudioRecordingRequest : DebugRequestWithResponse<StartAudioRecordingRequestArguments, StartAudioRecordingRequestResponse>
{
    public StartAudioRecordingRequest() : base("startAudioRecording")
    {
    }
}

internal class StopAudioRecordingRequest : DebugRequestWithResponse<StopAudioRecordingRequestArguments, StopAudioRecordingRequestResponse>
{
    public StopAudioRecordingRequest() : base("stopAudioRecording")
    {
    }
}

// Records the emulator's audio output to a WAV file (see BitMagic.X16Emulator/AudioRecorder.cs).
// The recording is taken from the emulator's own output buffer, so it is unaffected by the window
// being muted, and nothing is written while the emulator is stopped. Like keyboard/mouse input
// this observes rather than controls the session, so it's available to attach-only connections.
internal static class AudioRecordingRequestHandler
{
    // basePath is the launched project's base path, or null on a connection that didn't launch it.
    public static StartAudioRecordingRequestResponse HandleStartRequest(StartAudioRecordingRequestArguments? arguments, ServiceManager serviceManager, string? basePath)
    {
        if (arguments == null || string.IsNullOrWhiteSpace(arguments.Path))
            return new StartAudioRecordingRequestResponse { Success = false, Error = "No path provided." };

        if (!ServiceManagerFactory.IsSessionActive)
            return new StartAudioRecordingRequestResponse { Success = false, Error = "No active debug session." };

        string path;
        if (System.IO.Path.IsPathRooted(arguments.Path))
            path = System.IO.Path.GetFullPath(arguments.Path);
        else if (!string.IsNullOrWhiteSpace(basePath))
            path = System.IO.Path.GetFullPath(arguments.Path, basePath);
        else
            return new StartAudioRecordingRequestResponse { Success = false, Error = "Path must be absolute when the session's project base path isn't known to this connection." };

        try
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var recorder = serviceManager.StartAudioRecording(path);
            return new StartAudioRecordingRequestResponse { Success = true, Path = recorder.Path };
        }
        catch (Exception e)
        {
            return new StartAudioRecordingRequestResponse { Success = false, Error = e.Message };
        }
    }

    public static StopAudioRecordingRequestResponse HandleStopRequest(ServiceManager serviceManager)
    {
        var recorder = serviceManager.StopAudioRecording();

        if (recorder == null)
            return new StopAudioRecordingRequestResponse { Success = false, Error = "Not recording." };

        return new StopAudioRecordingRequestResponse
        {
            Success = recorder.Error == null,
            Error = recorder.Error == null ? null : $"Recording stopped early: {recorder.Error.Message}",
            Path = recorder.Path,
            Frames = recorder.Frames,
            Seconds = recorder.Seconds
        };
    }
}

public class StartAudioRecordingRequestArguments : DebugRequestArguments
{
    // WAV file to write. Relative paths are resolved against the project's base path.
    public string Path { get; set; } = "";
}

public class StartAudioRecordingRequestResponse : ResponseBody
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Path { get; set; }
}

public class StopAudioRecordingRequestArguments : DebugRequestArguments
{
}

public class StopAudioRecordingRequestResponse : ResponseBody
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Path { get; set; }
    public long Frames { get; set; }
    public double Seconds { get; set; }
}

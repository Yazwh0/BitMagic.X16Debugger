using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.CustomMessage;

// Silences the audio output while set, eg while scrubbing through a frame from the Frame Control view. On top of the
// project's muteAudio, so turning it off doesn't unmute a project that's muted anyway. Emulation is unaffected.

internal class SetMuteRequest : DebugRequestWithResponse<SetMuteRequestArguments, SetMuteRequestResponse>
{
    public SetMuteRequest() : base("setMute")
    {
    }
}

public class SetMuteRequestArguments : DebugRequestArguments
{
    public bool Muted { get; set; }
}

public class SetMuteRequestResponse : ResponseBody
{
    public bool Muted { get; set; }
}

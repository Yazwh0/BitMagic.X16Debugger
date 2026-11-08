using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.CustomMessage;

// Clicking the Frame Control image runs to that beam position. So that each click runs from the same place, the
// state when it was first clicked is kept as an anchor and put back before each run after that. Reset takes a new
// anchor from where the emulator is stopped now, which the client does when it stopped for some other reason.
// Only while stopped. X and Y set where to stop, in the same call so it's there before the client continues.

internal class BeamScrubRequest : DebugRequestWithResponse<BeamScrubRequestArguments, BeamScrubRequestResponse>
{
    public BeamScrubRequest() : base("beamScrub")
    {
    }
}

public class BeamScrubRequestArguments : DebugRequestArguments
{
    public bool Reset { get; set; }
    public int? X { get; set; } // the beam position to run to, a one shot breakpoint that VSCode doesn't see
    public int? Y { get; set; }
}

public class BeamScrubRequestResponse : ResponseBody
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public bool Restored { get; set; } // false if a new anchor was taken
}

using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;

namespace BitMagic.X16Debugger.CustomMessage;

// A request for the debug mode, see Modes/IX16DebugMode.CustomRequest. Lets a mode's own extension ask it
// things the standard requests don't cover, eg the BASIC mode's live view while the program runs.

internal class ModeRequest : DebugRequestWithResponse<ModeRequestArguments, ModeRequestResponse>
{
    public ModeRequest() : base("x16Mode")
    {
    }
}

public class ModeRequestArguments : DebugRequestArguments
{
    public string Command { get; set; } = "";
    public JObject? Arguments { get; set; }
}

public class ModeRequestResponse : ResponseBody
{
    public JToken? Result { get; set; }
}

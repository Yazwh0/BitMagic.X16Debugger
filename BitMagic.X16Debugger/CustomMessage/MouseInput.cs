using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.CustomMessage;

internal class MouseInputRequest : DebugRequestWithResponse<MouseInputRequestArguments, MouseInputRequestResponse>
{
    public MouseInputRequest() : base("mouseInput")
    {
    }
}

// Injects a single mouse movement/button-state sample into the emulator's own SMC mouse
// buffer - the exact same SmcBuffer.PushMouse call the standalone X16E window makes from
// its native OS mouse events (BitMagic.X16Emulator.Display/EmulatorWindow.cs). The X16's
// PS/2-style mouse protocol is relative, not absolute: DeltaX/DeltaY move the cursor from
// wherever it currently is, there's no "move to X,Y".
internal static class MouseInputRequestHandler
{
    public static MouseInputRequestResponse HandleRequest(MouseInputRequestArguments? arguments, Emulator emulator)
    {
        if (arguments == null)
            return new MouseInputRequestResponse { Success = false, Error = "No arguments provided." };

        var buttons = SmcBuffer.MouseButtons.None;
        if (arguments.Left) buttons |= SmcBuffer.MouseButtons.Left;
        if (arguments.Right) buttons |= SmcBuffer.MouseButtons.Right;
        if (arguments.Middle) buttons |= SmcBuffer.MouseButtons.Middle;

        emulator.SmcBuffer.PushMouse(arguments.DeltaX, arguments.DeltaY, buttons);

        return new MouseInputRequestResponse { Success = true };
    }
}

public class MouseInputRequestArguments : DebugRequestArguments
{
    public int DeltaX { get; set; }
    public int DeltaY { get; set; }
    public bool Left { get; set; }
    public bool Right { get; set; }
    public bool Middle { get; set; }
}

public class MouseInputRequestResponse : ResponseBody
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Silk.NET.Input;

namespace BitMagic.X16Debugger.CustomMessage;

internal class KeyboardInputRequest : DebugRequestWithResponse<KeyboardInputRequestArguments, KeyboardInputRequestResponse>
{
    public KeyboardInputRequest() : base("keyboardInput")
    {
    }
}

// Injects a single key event into the emulator's own SMC keyboard buffer - the exact same
// SmcBuffer.KeyDown/KeyUp calls the standalone X16E window makes from its native OS key
// events (BitMagic.X16Emulator.Display/EmulatorWindow.cs). One event per request, matching
// how a real keyboard delivers events, rather than a batch of several at once.
internal static class KeyboardInputRequestHandler
{
    public static KeyboardInputRequestResponse HandleRequest(KeyboardInputRequestArguments? arguments, Emulator emulator)
    {
        if (arguments == null)
            return new KeyboardInputRequestResponse { Success = false, Error = "No arguments provided." };

        if (!Enum.TryParse<Key>(arguments.Key, ignoreCase: true, out var key))
            return new KeyboardInputRequestResponse { Success = false, Error = $"Unknown key '{arguments.Key}'." };

        if (arguments.Down)
            emulator.SmcBuffer.KeyDown(key);
        else
            emulator.SmcBuffer.KeyUp(key);

        return new KeyboardInputRequestResponse { Success = true };
    }
}

public class KeyboardInputRequestArguments : DebugRequestArguments
{
    // Name of a Silk.NET.Input.Key value, e.g. "A", "Enter", "ShiftLeft", "F5" - the same enum
    // SmcBuffer.KeyDown/KeyUp already take, so any name that enum recognises works here too.
    public string Key { get; set; } = string.Empty;
    public bool Down { get; set; }
}

public class KeyboardInputRequestResponse : ResponseBody
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

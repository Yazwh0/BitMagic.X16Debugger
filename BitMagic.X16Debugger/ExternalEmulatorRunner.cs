
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using System.Diagnostics;

namespace BitMagic.X16Debugger;

internal static class ExternalEmulatorRunner
{
    public static void Run(X16Debug debug)
    {
        if (string.IsNullOrWhiteSpace(debug.OfficialEmulatorLocation))
        {
            debug.Logger.LogError("No path for the official emulator defined. Cannot continue.");
            debug.Protocol.SendEvent(new ExitedEvent(1));
            debug.Protocol.SendEvent(new TerminatedEvent());
            return;
        }

        debug.SetupSdCard();

        var parameters = debug.OfficialEmulatorParams ?? "";

        if (!parameters.Contains("-debug"))
            parameters += " -debug";

        if (!parameters.Contains("-midline-effects"))
            parameters += " -midline-effects";

        if (!parameters.Contains("-sdcard"))
        {
            if (!string.IsNullOrWhiteSpace(debug._debugProject!.SdCardOutput))
            {
                parameters += $" -sdcard \"{debug._debugProject!.SdCardOutput}\"";
            }
            else
            {
                debug.Logger.LogError("Warning no SDCard filename defined, nothing will run. Use 'sdCardOutput' in the project.json.");
            }
        }

        using var process = new Process();

        process.StartInfo.FileName = Path.Combine(debug.OfficialEmulatorLocation, "x16emu");
        process.StartInfo.Arguments = parameters;
        process.StartInfo.WorkingDirectory = debug.OfficialEmulatorLocation;

        process.Start();
        process.WaitForExit();

        debug.Protocol.SendEvent(new ExitedEvent(0));
        debug.Protocol.SendEvent(new TerminatedEvent());
    }
}

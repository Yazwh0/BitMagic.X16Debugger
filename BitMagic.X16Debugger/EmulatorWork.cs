using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static BitMagic.X16Emulator.Emulator;

namespace BitMagic.X16Debugger;

internal static class EmulatorWork
{
    public static Emulator? Emulator { get; set; }
    public static bool Done { get; set; } = false;

    public static void DoWork()
    {
        EmulatorResult result = EmulatorResult.ExitCondition;

        while (!Done)
        {
            if (Emulator != null)
                result = Emulator.Emulate();

            Done = true;
        }
    }
}

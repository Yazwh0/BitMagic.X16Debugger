using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BitMagic.X16Debugger;

internal static class AddressFunctions
{
    internal static int GetDebuggerAddress(int address, int ramBank, int romBank) =>
        (address, ramBank, romBank) switch
        {
            ( >= 0xc000, _, _) => ((romBank & 0xff) << 16) + (address & 0xffff),
            ( >= 0xa000, _, _) => ((ramBank & 0xff) << 16) + (address & 0xffff),
            _ => address
        };

    internal static string GetDebuggerAddressString(int address, int ramBank, int romBank) =>
        (address, ramBank, romBank) switch
        {
            ( >= 0xc000, _, _) => $"0x{((romBank & 0xff) << 16) + (address & 0xffff):X6}",
            ( >= 0xa000, _, _) => $"0x{((ramBank & 0xff) << 16) + (address & 0xffff):X6}",
            _ => $"0x{address:X6}"
        };

    internal static string GetDebuggerAddressDisplayString(int address, int ramBank, int romBank) =>
        (address, ramBank, romBank) switch
        {
            ( >= 0xc000, _, _) => $"{(romBank & 0xff):X2}:{address & 0xffff:X4}",
            ( >= 0xa000, _, _) => $"{(ramBank & 0xff):X2}:{address & 0xffff:X4}",
            _ => $"00:{address:X4}"
        };

    internal static string GetDebuggerAddressDisplayString(int debuggerAddress) =>
        debuggerAddress >= 0xa000 ?
        $"{(debuggerAddress & 0xff0000) >> 16:X2}:{debuggerAddress & 0xffff:X4}" :
        $"00:{debuggerAddress:X4}";

    internal static (int Address, int RamBank, int RomBank) GetMachineAddress(int debuggerAddress) =>
         (debuggerAddress & 0xffff) switch
         {
             >= 0xc000 => (debuggerAddress & 0xffff, 0, (debuggerAddress & 0xff0000) >> 16),
             >= 0xa000 => (debuggerAddress & 0xffff, (debuggerAddress & 0xff0000) >> 16, 0),
             _ => (debuggerAddress & 0xffff, 0, 0)
         };

}

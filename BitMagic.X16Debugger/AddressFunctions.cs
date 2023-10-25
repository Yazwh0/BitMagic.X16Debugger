using BitMagic.X16Emulator;

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
    internal static int GetDebuggerAddress(int address, Emulator emulator) => 
        GetDebuggerAddress(address, (int)emulator.RamBankAct, (int)emulator.RomBankAct);

    internal static (int Address, int RamBank, int RomBank) GetAddress(int debuggerAddress) =>
        (debuggerAddress) switch
        {
            >= 0xc000 => (debuggerAddress & 0xffff, 0, (debuggerAddress & 0xff0000) >> 16),
            >= 0xa000 => (debuggerAddress & 0xffff, (debuggerAddress & 0xff0000) >> 16, 0),
            _ => (debuggerAddress & 0xffff, 0, 0)
        };

    internal static (int Address, int Bank) GetAddressBank(int debuggerAddress) =>
        (debuggerAddress) switch
        {
            >= 0xc000 => (debuggerAddress & 0xffff, (debuggerAddress & 0xff0000) >> 16),
            >= 0xa000 => (debuggerAddress & 0xffff, (debuggerAddress & 0xff0000) >> 16),
            _ => (debuggerAddress & 0xffff, 0)
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

    // returns the location in the break point array for a given bank\address
    // second value is returned if the address is currently the active bank
    // breakpoint array:
    // Start      End (-1)     0x:-
    //       0 =>   10,000   : active memory
    //  10,000 =>  210,000   : ram banks
    // 210,000 =>  610,000   : rom banks
    internal static (int address, int secondAddress) GetMemoryLocations(int bank, int address) =>
        (bank, address) switch
        {
            (_, < 0xa000) => (address, 0),
            (_, < 0xc000) => (address, 0x10000 + bank * 0x2000 + address - 0xa000),
            _ => (address, 0x210000 + (bank * 0x4000) + address - 0xc000)
        };

    internal static (int address, int secondAddress) GetMemoryLocations(int debuggerAddress) =>
        (debuggerAddress & 0xffff) switch
        {
            < 0xa000 => (debuggerAddress, 0),
            < 0xc000 => (debuggerAddress & 0xffff, 0x10000 + (debuggerAddress & 0xffff) - 0xa000 + ((debuggerAddress & 0xff0000) >> 16) * 0x2000), // Ram
            _ => (debuggerAddress & 0xffff, 0x210000 + (debuggerAddress & 0xffff) - 0xc000 + ((debuggerAddress & 0xff0000) >> 16) * 0x4000), // Rom
        };

    internal static (int address, int secondAddress) GetMemoryLocations(int address, Emulator emulator) =>
        GetMemoryLocations(GetDebuggerAddress(address, emulator));
}

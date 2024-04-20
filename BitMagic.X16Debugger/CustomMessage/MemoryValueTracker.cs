using BitMagic.Common.Address;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.CustomMessage;

internal class MemoryValueTracker : DebugRequestWithResponse<MemoryValueTrackerArguments, MemoryValueTrackerResponse>
{
    public MemoryValueTracker() : base("getMemoryValueLocations")
    {
    }
}

internal static class MemoryValueTrackerHandler
{
    public static MemoryValueTrackerResponse HandleRequest(MemoryValueTrackerArguments? arguments, Emulator emulator)
    {
        if (arguments == null)
            return new MemoryValueTrackerResponse();

        if (arguments.Locations == null || !arguments.Locations.Any())
            return FindInitial(arguments.ToFind, GetSearchWidth(arguments.SearchWidth), emulator);

        return FindInstances(arguments, emulator);
    }

    private static SearchType GetSearchType(string search) => search switch
    {
        "Equal" => SearchType.Equal,
        "Not Equal" => SearchType.NotEqual,
        "Less Than" => SearchType.LessThan,
        "Greater Than" => SearchType.GreaterThan,
        _ => SearchType.Equal
    };

    private static SearchWidth GetSearchWidth(string searchWidth) => searchWidth switch
    {
        "Byte" => SearchWidth.Byte,
        "Word" => SearchWidth.Word
    };

    private static bool IsMatch(uint a, uint b, SearchType searchType) => searchType switch
    {
        SearchType.Equal => a == b,
        SearchType.NotEqual => a != b,
        SearchType.GreaterThan => a > b,
        SearchType.LessThan => a < b
    };

    private static MemoryValueTrackerResponse FindInitial(uint toFind, SearchWidth width, Emulator emulator)
    {
        var matches = new List<MemoryValue>();

        var adjust = width == SearchWidth.Byte ? 0 : -1;
        for (var i = 0; i < 0xa000 + adjust; i++)
        {
            if (GetValue(emulator.Memory, i, width) == toFind)
                matches.Add(new MemoryValue() { Location = i, Value = GetValue(emulator.Memory, i, width) });
        }

        // for active bank, check main ram, but store debugger https://discord.com/channels/547559626024157184/1168970882119647283address as it could be switched out next time
        for (var i = 0xa000; i < 0xc000 + adjust; i++)
        {
            var debuggerAddress = AddressFunctions.GetDebuggerAddress(i, (int)emulator.RamBankAct, 0);

            if (GetValue(emulator.Memory, i, width) == toFind)
                matches.Add(new MemoryValue() { Location = debuggerAddress, Value = GetValue(emulator.Memory, i, width) });
        }

        for (var bank = 0; bank < 256; bank++)
        {
            if (bank == emulator.RamBankAct)
                continue;

            for (var i = 0xa000; i < 0xc000 + adjust; i++)
            {
                var debuggerAddress = AddressFunctions.GetDebuggerAddress(i, bank, 0);

                var (_, address) = AddressFunctions.GetMemoryLocations(debuggerAddress);

                if (GetValue(emulator.RamBank, address - 0x10000, width) == toFind)
                    matches.Add(new MemoryValue() { Location = debuggerAddress, Value = GetValue(emulator.RamBank, address - 0x10000, width) });
            }
        }

        return new MemoryValueTrackerResponse() { ToFind = toFind, Locations = matches, Stepping = emulator.Stepping, SearchType = "Equal" };
    }

    private static uint GetValue(Span<byte> memory, int index, SearchWidth width) => width switch
    {
        SearchWidth.Byte => memory[index],
        SearchWidth.Word => (uint)(memory[index] + (memory[index + 1] << 8))
    };

    private static MemoryValueTrackerResponse FindInstances(MemoryValueTrackerArguments arguments, Emulator emulator)
    {
        var matches = new List<MemoryValue>();
        var searchType = GetSearchType(arguments.SearchType);
        var width = GetSearchWidth(arguments.SearchWidth);

        if (arguments.Locations == null)
            return new MemoryValueTrackerResponse() { ToFind = arguments.ToFind }; // error

        foreach (var i in arguments.Locations)
        {
            // Main ram
            if (i.Location < 0xa000)
            {
                if (IsMatch(GetValue(emulator.Memory, i.Location, width), arguments.ToFind, searchType))
                    matches.Add(new MemoryValue() { Location = i.Location, Value = GetValue(emulator.Memory, i.Location, width) });

                continue;
            }

            // banked
            var (address, bank) = AddressFunctions.GetAddressBank(i.Location);

            // banked and current bank
            if (bank == emulator.RamBankAct)
            {
                if (IsMatch(GetValue(emulator.Memory, address, width), arguments.ToFind, searchType))
                    matches.Add(new MemoryValue() { Location = i.Location, Value = GetValue(emulator.Memory, address, width) });

                continue;
            }

            var (_, bankAddress) = AddressFunctions.GetMemoryLocations(i.Location);

            if (IsMatch(GetValue(emulator.RamBank, bankAddress - 0xa000, width), arguments.ToFind, searchType))
                matches.Add(new MemoryValue() { Location = i.Location, Value = GetValue(emulator.RamBank, bankAddress - 0xa000, width) });
        }

        return new MemoryValueTrackerResponse() { ToFind = arguments.ToFind, Locations = matches, Stepping = emulator.Stepping, SearchType = arguments.SearchType };
    }
}

public class MemoryValueTrackerArguments : DebugRequestArguments
{
    public string SearchWidth { get; set; } = "";
    public uint ToFind { get; set; }
    public string SearchType { get; set; } = "";
    public MemoryValue[]? Locations { get; set; }
}

public class MemoryValueTrackerResponse : ResponseBody
{
    public uint ToFind { get; set; }
    public string SearchType { get; set; } = "";
    public List<MemoryValue>? Locations { get; set; }
    public bool Stepping { get; set; }
}

public record MemoryValue
{
    public int Location { get; set; }
    public uint Value { get; set; }
}

internal enum SearchType
{
    Equal,
    NotEqual,
    LessThan,
    GreaterThan
}

internal enum SearchWidth
{
    Byte,
    Word
}
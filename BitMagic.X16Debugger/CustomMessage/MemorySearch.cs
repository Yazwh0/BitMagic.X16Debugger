using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.CustomMessage;

internal class MemorySearchRequest : DebugRequestWithResponse<MemorySearchArguments, MemorySearchResponse>
{
    public MemorySearchRequest() : base("searchMemory")
    {
    }
}

internal static class MemorySearchHandler
{
    public static MemorySearchResponse HandleRequest(MemorySearchArguments? arguments, Emulator emulator)
    {
        var toReturn = new MemorySearchResponse();

        if (arguments == null || string.IsNullOrEmpty(arguments.Pattern))
            return toReturn;

        var data = MemorySpaceResolver.Resolve(arguments.MemoryReference, emulator, out _);
        toReturn.SpaceLength = data.Length;

        byte[] pattern;
        try
        {
            pattern = Convert.FromBase64String(arguments.Pattern);
        }
        catch (FormatException)
        {
            return toReturn;
        }

        if (pattern.Length == 0 || data.Length < pattern.Length)
            return toReturn;

        var maxResults = arguments.MaxResults > 0 ? arguments.MaxResults : 500;
        var matches = new List<int>();

        for (var i = 0; i <= data.Length - pattern.Length; i++)
        {
            if (!IsMatchAt(data, i, pattern, arguments.CaseInsensitive))
                continue;

            matches.Add(i);
            if (matches.Count >= maxResults)
            {
                toReturn.Truncated = true;
                break;
            }
        }

        toReturn.Matches = matches;
        return toReturn;
    }

    private static bool IsMatchAt(Span<byte> data, int offset, byte[] pattern, bool caseInsensitive)
    {
        for (var j = 0; j < pattern.Length; j++)
        {
            var a = data[offset + j];
            var b = pattern[j];

            if (caseInsensitive)
            {
                a = ToLowerByte(a);
                b = ToLowerByte(b);
            }

            if (a != b)
                return false;
        }
        return true;
    }

    private static byte ToLowerByte(byte b) => (b >= 0x41 && b <= 0x5a) ? (byte)(b + 0x20) : b;
}

public class MemorySearchArguments : DebugRequestArguments
{
    public string MemoryReference { get; set; } = "";
    public string Pattern { get; set; } = ""; // base64-encoded byte pattern
    public bool CaseInsensitive { get; set; }
    public int MaxResults { get; set; } = 500;
}

public class MemorySearchResponse : ResponseBody
{
    public List<int> Matches { get; set; } = new();
    public bool Truncated { get; set; }
    public int SpaceLength { get; set; }
}

using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using BitMagic.X16Emulator;

namespace BitMagic.X16Debugger.CustomMessage;

internal class MemoryUseRequest : DebugRequestWithResponse<MemoryUseRequestArguments, MemoryUseRequestResponse>
{
    public MemoryUseRequest() : base("getMemoryUse")
    {
    }
}

internal static class MemoryUseHandler
{
    private const uint MemoryChanged   = 0b00010000;
    private const uint MemoryHistoric  = 0b00100000;
    private const uint MemoryExecution = 0b01000000;
    private const uint MemoryRead      = 0b10000000;

    private static readonly Image<Rgba32> _image = new(256, 256);

    public unsafe static MemoryUseRequestResponse HandleRequest(MemoryUseRequestArguments? arguments, Emulator emulator)
    {
        var idx = 0;

        for(var y = 0; y < 256; y++)
        {
            _image.ProcessPixelRows(i =>
            {
                var memory = emulator.Breakpoints;
                var span = i.GetRowSpan(y);

                for (var x = 0; x < 256; x++)
                {
                    var val = memory[idx] ;

                    byte r = (val & MemoryExecution) == 0 ? (byte)0 : (byte)255;
                    byte g = (val & (MemoryChanged | MemoryHistoric)) switch
                    {
                        MemoryHistoric => (byte)20,
                        MemoryChanged + MemoryHistoric => (byte)255,
                        _ => (byte)0
                    };
                    byte b = (val & MemoryRead) == 0 ? (byte)0 : (byte)255;

                    span[x] = new Rgba32(r, g, b);

                    val &= ~(MemoryChanged | MemoryExecution | MemoryRead);
                    memory[idx] = val;

                    idx++;
                }
            });
        }

        var memoryStream = new MemoryStream();
        _image.SaveAsPng(memoryStream);
        var toReturn = new MemoryUseRequestResponse() { Display = Convert.ToBase64String(memoryStream.ToArray()) };
        return toReturn;
    }
}

public class MemoryUseRequestArguments : DebugRequestArguments
{
}

public class MemoryUseRequestResponse : ResponseBody
{
    public string Display { get; set; } = "";
}
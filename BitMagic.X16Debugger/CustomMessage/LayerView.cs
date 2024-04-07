using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Runtime.InteropServices;

namespace BitMagic.X16Debugger.CustomMessage;

internal class LayerRequest : DebugRequestWithResponse<LayerRequestArguments, LayerRequestResponse>
{
    public LayerRequest() : base("getLayers")
    {
    }
}

internal static class LayerRequestHandler
{
    private static readonly Image<Rgba32> _image = new(640, 480);

    [DllImport("msvcrt.dll", SetLastError = false)]
    static extern IntPtr memcpy(IntPtr dest, IntPtr src, int count);

    public unsafe static LayerRequestResponse HandleRequest(LayerRequestArguments? arguments, Emulator emulator)
    {
        var idx = 0;
        var toReturn = new LayerRequestResponse();

        for (var layer = 0; layer < 6; layer++)
        {
            _image.ProcessPixelRows(i =>
            {
                for (var y = 0; y < 480; y++)
                {
                    var span = i.GetRowSpan(y);

                    fixed (Rgba32* ptr = &MemoryMarshal.GetReference(span))
                    {
                        memcpy((IntPtr)ptr, (IntPtr)emulator.DisplayPtr + idx, 640 * 4);
                    }

                    idx += 800 * 4;
                }
            });

            idx += (525 - 480) * 800 * 4;
            var memoryStream = new MemoryStream();
            _image.SaveAsPng(memoryStream);

            toReturn.Display.Add(Convert.ToBase64String(memoryStream.ToArray()));
        }

        return toReturn;
    }
}

public class LayerRequestArguments : DebugRequestArguments
{
}

public class LayerRequestResponse : ResponseBody
{
    public List<string> Display { get; set; } = new();
}

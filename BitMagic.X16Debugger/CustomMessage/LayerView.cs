using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using BitMagic.Common;

namespace BitMagic.X16Debugger.CustomMessage;

internal class LayerRequest : DebugRequestWithResponse<LayerRequestArguments, LayerRequestResponse>
{
    public LayerRequest() : base("getLayers")
    {
    }
}

internal static class LayerRequestHandler
{
    private static readonly RgbaImage _image = new(640, 480);

    public unsafe static LayerRequestResponse HandleRequest(LayerRequestArguments? arguments, Emulator emulator)
    {
        ulong idx = 0;
        var toReturn = new LayerRequestResponse();

        for (var layer = 0; layer < 6; layer++)
        {
            for (var y = 0; y < 480; y++)
            {
                fixed (PixelRgba* ptr = _image.GetRow(y))
                {
                    Buffer.MemoryCopy((void*)(emulator.DisplayPtr + idx), ptr, 640 * 4, 640 * 4);
                }

                idx += 800 * 4;
            }

            idx += (525 - 480) * 800 * 4;

            toReturn.Display.Add(Convert.ToBase64String(ImageFile.ToPng(_image)));
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

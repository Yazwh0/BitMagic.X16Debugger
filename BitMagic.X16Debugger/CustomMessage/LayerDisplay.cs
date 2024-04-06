using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections;
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
    private const int _headerLength = 70;
    private static byte[] _displayBuffer = new byte[_headerLength + (640 * 480 * 4)];
    private static bool _displayInitialised = false;
    private static readonly Image<Rgba32> _image = new(640, 480);
    private static void InitialiseDisplayBuffer()
    {
        var idx = 0;
        AppendToDisplay(BitConverter.GetBytes((ushort)0x4d42), ref idx);
        AppendToDisplay(BitConverter.GetBytes((uint)640 * 480 * 4 + _headerLength), ref idx);
        AppendToDisplay(BitConverter.GetBytes((uint)0), ref idx);
        AppendToDisplay(BitConverter.GetBytes((uint)_headerLength), ref idx);

        AppendToDisplay(BitConverter.GetBytes((uint)40), ref idx); // header size
        AppendToDisplay(BitConverter.GetBytes((int)640), ref idx); // width
        AppendToDisplay(BitConverter.GetBytes((int)480), ref idx); // height

        AppendToDisplay(BitConverter.GetBytes((ushort)1), ref idx);
        AppendToDisplay(BitConverter.GetBytes((ushort)32), ref idx);

        AppendToDisplay(BitConverter.GetBytes((uint)6), ref idx);
        AppendToDisplay(BitConverter.GetBytes((uint)640 * 480 * 4), ref idx);

        AppendToDisplay(BitConverter.GetBytes((int)10000), ref idx);
        AppendToDisplay(BitConverter.GetBytes((int)10000), ref idx);

        AppendToDisplay(BitConverter.GetBytes((uint)0), ref idx);
        AppendToDisplay(BitConverter.GetBytes((uint)0), ref idx);

        AppendToDisplay(BitConverter.GetBytes((uint)0x000000FF), ref idx);
        AppendToDisplay(BitConverter.GetBytes((uint)0x0000FF00), ref idx);
        AppendToDisplay(BitConverter.GetBytes((uint)0x00FF0000), ref idx);
        AppendToDisplay(BitConverter.GetBytes((uint)0xFF000000), ref idx);

        _displayInitialised = true;
    }

    private static void AppendToDisplay(byte[] data, ref int index)
    {
        for (var i = 0; i < data.Length; i++)
            _displayBuffer[index++] = data[i];
    }

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


    public static LayerRequestResponse HandleRequesxt(LayerRequestArguments? arguments, Emulator emulator)
    {
        if (!_displayInitialised) InitialiseDisplayBuffer();

        var idx = 0;
        var display = emulator.DisplayRaw;
        var toReturn = new LayerRequestResponse();

        for (var layer = 0; layer < 6; layer++)
        {
            var dIdx = _headerLength;

            for (var y = 0; y < 480; y++)
            {
                for (var x = 0; x < 640; x++)
                {
                    _displayBuffer[dIdx++] = display[idx++];
                    _displayBuffer[dIdx++] = display[idx++];
                    _displayBuffer[dIdx++] = display[idx++];
                    _displayBuffer[dIdx++] = display[idx++];
                }
                idx += (800 - 640) * 4;
            }
            idx += (525 - 480) * 4;

            File.WriteAllBytes($"c:\\temp\\file_{layer}.bmp", _displayBuffer);

            toReturn.Display.Add(Convert.ToBase64String(_displayBuffer));
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

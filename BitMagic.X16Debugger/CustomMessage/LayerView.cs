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

internal class ScreenRequest : DebugRequestWithResponse<ScreenRequestArguments, ScreenRequestResponse>
{
    public ScreenRequest() : base("getScreen")
    {
    }
}

internal static class ScreenRequestHandler
{
    private const int VisibleWidth = 640;
    private const int VisibleHeight = 480;
    private const int FullWidth = 800;
    private const int FullHeight = 525;

    private static readonly RgbaImage _image = new(VisibleWidth, VisibleHeight);
    private static readonly RgbaImage _fullImage = new(FullWidth, FullHeight);

    // the six vera layers blended in compositing order, the same way X16E's window does (SrcAlpha, OneMinusSrcAlpha).
    // the debug layer (7th) is left out so this is what the hardware would show.
    // fullFrame returns the whole 800x525 beam area, with the part outside the visible screen dimmed and the beam marked.
    // a render mode other than Beam only applies while the emulator is stopped, otherwise it's the display as it is.
    // RenderToX and RenderToY are a preview: the display only drawn on from where it's stopped to that point, wrapping
    // into the next frame if it's behind the beam. They take the place of the render mode.
    public static ScreenRequestResponse HandleRequest(ScreenRequestArguments? arguments, Emulator emulator, Func<Action, bool>? runIfStopped)
    {
        var fullFrame = arguments?.FullFrame ?? false;
        var mode = ParseRenderMode(arguments?.RenderMode);
        (int X, int Y)? renderTo = arguments?.RenderToX != null && arguments.RenderToY != null
            ? (Math.Clamp(arguments.RenderToX.Value, 0, FullWidth - 1), Math.Clamp(arguments.RenderToY.Value, 0, FullHeight - 1))
            : null;
        var rendered = RenderModeName(Emulator.DisplayRenderMode.Beam);
        var (beamX, beamY) = (emulator.Vera.Beam_X, emulator.Vera.Beam_Y); // rendering ahead moves the beam on
        RgbaImage? image = null;

        if ((renderTo != null || mode != Emulator.DisplayRenderMode.Beam) && runIfStopped != null)
        {
            runIfStopped(() =>
            {
                var cycles = renderTo != null ? emulator.CyclesToBeam(renderTo.Value.X, renderTo.Value.Y) : emulator.RenderAheadCycles(mode);

                emulator.RenderAhead(cycles, () => image = Render(emulator, fullFrame, beam: (beamX, beamY)));
                rendered = renderTo != null ? Preview : RenderModeName(mode);
            });
        }

        image ??= Render(emulator, fullFrame);

        return new ScreenRequestResponse
        {
            Display = Convert.ToBase64String(ImageFile.ToPng(image)),
            Width = image.Width,
            Height = image.Height,
            Frame = emulator.Vera.Frame_Count,
            BeamX = beamX,
            BeamY = beamY,
            RenderMode = rendered
        };
    }

    public const string Preview = "preview"; // the RenderMode in the response when it's rendered to RenderToX, RenderToY

    public static Emulator.DisplayRenderMode ParseRenderMode(string? mode) => mode switch
    {
        null or "" or "beam" => Emulator.DisplayRenderMode.Beam,
        "finishFrame" => Emulator.DisplayRenderMode.FinishFrame,
        "fromState" => Emulator.DisplayRenderMode.FromState,
        _ => throw new ArgumentException($"Unknown render mode '{mode}'.", nameof(mode))
    };

    public static string RenderModeName(Emulator.DisplayRenderMode mode) => mode switch
    {
        Emulator.DisplayRenderMode.FinishFrame => "finishFrame",
        Emulator.DisplayRenderMode.FromState => "fromState",
        _ => "beam"
    };

    // without an image to draw into, the image is shared between getScreen calls. pass one in from anywhere else,
    // as a snapshot can be taken on the debug thread while getScreen is streaming. beam is where to mark the beam, if
    // it's not where vera has it.
    public unsafe static RgbaImage Render(Emulator emulator, bool fullFrame, RgbaImage? into = null, (int X, int Y)? beam = null)
    {
        const int layerSize = FullWidth * FullHeight;
        var display = (PixelRgba*)emulator.DisplayPtr;
        var image = into ?? (fullFrame ? _fullImage : _image);

        if (image.Width != (fullFrame ? FullWidth : VisibleWidth) || image.Height != (fullFrame ? FullHeight : VisibleHeight))
            throw new ArgumentException("Image is the wrong size.", nameof(into));

        for (var y = 0; y < image.Height; y++)
        {
            var row = image.GetRow(y);

            for (var x = 0; x < image.Width; x++)
            {
                var pixelIdx = y * 800 + x;
                int r = 0, g = 0, b = 0;

                for (var layer = 0; layer < 6; layer++)
                {
                    var src = display[layer * layerSize + pixelIdx];
                    var a = src.A;

                    if (a == 0)
                        continue;

                    if (a == 0xff)
                    {
                        r = src.R; g = src.G; b = src.B;
                        continue;
                    }

                    r = (src.R * a + r * (255 - a) + 127) / 255;
                    g = (src.G * a + g * (255 - a) + 127) / 255;
                    b = (src.B * a + b * (255 - a) + 127) / 255;
                }

                if (x >= VisibleWidth || y >= VisibleHeight)
                {
                    r = r / 3 + 32;
                    g = g / 3 + 32;
                    b = b / 3 + 32;
                }

                row[x] = new PixelRgba((byte)r, (byte)g, (byte)b);
            }
        }

        var beamX = beam?.X ?? emulator.Vera.Beam_X;
        var beamY = beam?.Y ?? emulator.Vera.Beam_Y;

        if (fullFrame)
        {
            // visible screen edges, dotted like the cpu profiler
            for (var y = 1; y < VisibleHeight; y += 2)
                image[VisibleWidth, y] = new PixelRgba(200, 200, 200);

            for (var x = 1; x < VisibleWidth; x += 2)
                image[x, VisibleHeight] = new PixelRgba(200, 200, 200);

            // beam position, how far the display has been drawn
            var beamColour = new PixelRgba(255, 0, 0);
            for (var i = -4; i <= 4; i++)
            {
                if (beamX + i >= 0 && beamX + i < FullWidth)
                    image[beamX + i, beamY] = beamColour;

                if (beamY + i >= 0 && beamY + i < FullHeight)
                    image[beamX, beamY + i] = beamColour;
            }
        }

        return image;
    }
}

public class ScreenRequestArguments : DebugRequestArguments
{
    public bool FullFrame { get; set; }
    public string? RenderMode { get; set; } // beam, finishFrame or fromState
    public int? RenderToX { get; set; } // preview to this beam position while stopped, see ScreenRequestHandler
    public int? RenderToY { get; set; }
}

public class ScreenRequestResponse : ResponseBody
{
    public string Display { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public uint Frame { get; set; }
    public int BeamX { get; set; }
    public int BeamY { get; set; }
    public string RenderMode { get; set; } = "beam"; // what was used, beam while running, preview for RenderToX, RenderToY
}

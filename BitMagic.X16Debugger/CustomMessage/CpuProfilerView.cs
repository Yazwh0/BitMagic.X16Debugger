using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System.Drawing;

namespace BitMagic.X16Debugger.CustomMessage;

public class CpuProfilerRequest : DebugRequestWithResponse<CpuProfilerArguements, CpuProfilerResponse>
{
    public CpuProfilerRequest() : base("getCpuProfile")
    {
    }
}

internal static class CpuProfilerRequestHandler
{
    private static readonly Image<Rgba32> _image = new(800, 525);
    private static readonly List<CpuProfilerLineItem> _cpuProfilerLineItems = new ()
    {
        new ("true", LineType.BuiltIn, "Default", "", "", "")
    };
    private static readonly List<Func<ushort, byte, byte, byte, byte, byte, byte, ushort, byte, byte, ushort, ulong, Rgba32?>> _pens = new();

    public static CpuProfilerResponse HandleRequest(CpuProfilerArguements? arguments, Emulator emulator, SourceMapManager sourceMapManager, DebugableFileManager debugableFileManager)
    {
        if (arguments == null) throw new ArgumentNullException(nameof(arguments));

        if (arguments.Items != null && arguments.Items.Count != 0)
            UpdateCpuProfilerRules(arguments.Items);

        return GetCpuHistory(emulator);
    }

    private static void UpdateCpuProfilerRules(List<CpuProfilerLineItem> items)
    {
        _cpuProfilerLineItems.Clear();
        _cpuProfilerLineItems.AddRange(items);
        GeneratePens();
    }

    private static void GeneratePens()
    {
        _pens.Clear();
        foreach (var i in _cpuProfilerLineItems)
        {
            if (string.IsNullOrWhiteSpace(i.Predicate))
                i.Predicate = "true";

            switch (i.LineType)
            {
                case LineType.BuiltIn:
                    switch (i.Definition)
                    {
                        case "PC":
                            _pens.Add(BuildColorFunc(i.Predicate, "new Rgba32((byte)(PC & 0xff), (byte)((PC & 0xff00) >> 8), (byte)((PC > 0xc000 ? RomBank : (PC > 0xa000 ? RamBank : 0))))"));
                            break;
                        case "Op Code":
                            _pens.Add(BuildColorFunc(i.Predicate, "new Rgba32((byte)((OpCode & 0x07) << 5), (byte)((OpCode & 0x38) << 2), (byte)(OpCode & 0xc0))"));
                            break;
                        default:
                            _pens.Add(BuildColorFunc(i.Predicate, "new Rgba32(0, 0, 0)"));
                            break;
                    }
                    break;
                case LineType.Colour:
                    {
                        // Try to parse the CSS colour string (including "#..." and named colours).
                        // Use System.Drawing.ColorTranslator.FromHtml which supports #hex and named colours.
                        // On failure, fall back to black.
                        try
                        {
                            if (string.IsNullOrWhiteSpace(i.Definition))
                                throw new ArgumentException("empty definition");

                            var col = ColorTranslator.FromHtml(i.Definition.Trim());
                            // store numeric components back into the item (they are strings in the model)
                            i.R = col.R.ToString();
                            i.G = col.G.ToString();
                            i.B = col.B.ToString();

                            _pens.Add(BuildColorFunc(i.Predicate, $"new Rgba32((byte){col.R}, (byte){col.G}, (byte){col.B})"));
                        }
                        catch
                        {
                            // invalid colour - treat as black
                            i.R = "0";
                            i.G = "0";
                            i.B = "0";
                            _pens.Add(BuildColorFunc(i.Predicate, "new Rgba32(0, 0, 0)"));
                        }
                    }
                    break;
                case LineType.Rgb:
                    if (string.IsNullOrWhiteSpace(i.R))
                        i.R = "0";
                    if (string.IsNullOrWhiteSpace(i.G))
                        i.G = "0";
                    if (string.IsNullOrWhiteSpace(i.B))
                        i.B = "0";
                    _pens.Add(BuildColorFunc(i.Predicate, $"new Rgba32((byte)({i.R}), (byte)({i.G}), (byte)({i.B}))"));
                    break;
                default:
                    _pens.Add(BuildColorFunc("false", "new Rgba32(0, 0, 0)"));
                    break;
            }
        }
    }

    public static CpuProfilerResponse GetCpuHistory(Emulator emulator)
    {
        var toReturn = new CpuProfilerResponse();

        var history = emulator.History;

        // search backwards looking for a cpu_y switch to 525 -> 0. Capture the index
        var idx = (int)emulator.HistoryPosition - 1;

        var screenEndIdx = -1;
        var screenStartIdx = -1;
        var frameNumber = emulator.State.Frame_Count;
        //Console.WriteLine($"IDX              : {idx}");

        bool searching = true;
        //for (var i = 0; i < idx; i++)
        //{
        //    if (history[i].CpuY == 0 && searching)
        //    {
        //        Console.WriteLine($"Y : {i}");
        //        searching = false;
        //    }
        //    else if (history[i].CpuY != 0 && !searching)
        //        searching = true;
        //}

        var h = history[idx];
        var lastY = h.CpuY;
        while (true)
        {
            h = history[idx];
            if (h.CpuY > lastY)
            {
                //   Console.WriteLine($"First Y          : {idx}");
                //while (h.CpuY == 0)
                //{
                //    if (idx <= 0)
                //        idx = emulator.Options.HistorySize;
                //    idx--;
                //}
                //  Console.WriteLine($"After roll back Y : {idx}");

                if (screenEndIdx == -1)
                {
                    screenEndIdx = idx + 1;
                }
                else
                {
                    screenStartIdx = idx + 1;
                    break;
                }
            }

            lastY = h.CpuY;

            if (idx <= 0)
                idx = emulator.Options.HistorySize - 1;

            idx--;
        }

        var frameClock = history[screenStartIdx].Clock;
        h = history[screenStartIdx];
        // get the frame start time
        //var startClock = (ulong)(frameNumber * 8_000_000ul / (2500.0 / 42.0));

        var startClock = (frameClock / 134400) * 134400;

        var startOffset = frameClock - startClock;

        //var penFunc = BuildColorFunc("OpCode == 0xea", "new Rgba32(255, 255, (byte)((PC & 0xff) * 4))");

        idx = screenStartIdx;
        //ulong clk = 0;
        //ulong lastClock = history[idx - 1].Clock;
        _image.ProcessPixelRows(i =>
        {
            for (var j = 0; j < i.Height; j++)
            {
                i.GetRowSpan(j).Clear();
            }

            var history = emulator.History;
            var span = i.GetRowSpan(0);
            var thisRow = 0;
            var x = 0;
            var currentClkPos = 0ul;
            var lastIdxStart = 0;
            var nextOffset = 0;
            var offset = 0ul;
            Rgba32 colour = new Rgba32(0, 0, 0);

            while (true)
            {
                //
                // h.Clock is the time at the instruction,
                // Instruction time at idx is:
                // history[idx+1].Clock - h.Clock
                //
                h = history[idx];
                if (h.CpuY != thisRow)
                {
                    //var debug = lastIdxStart > idx ? [] : history[lastIdxStart..idx];
                    if (thisRow < 480 && (thisRow % 2) != 0)
                    {
                        span[640] = new Rgba32(200, 200, 200);
                    }

                    thisRow = h.CpuY;
                    span = i.GetRowSpan(thisRow);
                    currentClkPos = 0;
                    lastIdxStart = idx;
                    nextOffset = 0;

                    for (x = 0; x < nextOffset; x++)
                    {
                        span[x] = colour;
                    }
                }

                colour = GetPenColour(h);
                foreach (var pen in _pens)
                {
                    var c = pen(h.PC, h.OpCode, h.RomBank, h.RamBank, h.A, h.X, h.Y, h.Params, h.Flags, h.SP, h.CpuY, h.Clock);
                    if (c != null)
                    {
                        colour = c.Value;
                        break;
                    }
                }

                var nextIdx = idx + 1;
                if (nextIdx >= emulator.Options.HistorySize)
                    nextIdx = 0;

                var thisClk = (history[nextIdx].Clock - h.Clock);

                for (var xdraw = Math.Floor(currentClkPos * 3.125); xdraw < Math.Floor((currentClkPos + thisClk) * 3.125); xdraw++)
                {
                    if (x < 800)
                        span[x] = colour;
                    else
                        nextOffset++;

                    x++;
                }

                currentClkPos += thisClk;

                //clk += h.Clock - lastClock;

                if (idx == screenEndIdx)
                    break;

                //lastClock = h.Clock;
                idx = nextIdx;

            }
        });

        var memoryStream = new MemoryStream();
        _image.SaveAsPng(memoryStream);

        //var fs = new FileStream(@"c:\temp\test.png", FileMode.Create);
        //_image.SaveAsPng(fs);

        toReturn.Display = Convert.ToBase64String(memoryStream.ToArray());

        return toReturn;
    }

    public static Rgba32 GetPenColour(EmulatorHistory item)
    {
        return new Rgba32((byte)(item.PC & 0xff), (byte)((item.PC & 0xff00) >> 8), item.RamBank);
    }

    public static Func<ushort, byte, byte, byte, byte, byte, byte, ushort, byte, byte, ushort, ulong, Rgba32?> BuildColorFunc(string predicate, string expression)
    {
        string code =
            "new System.Func<ushort, byte, byte, byte, byte, byte, byte, ushort, byte, byte, ushort, ulong, SixLabors.ImageSharp.PixelFormats.Rgba32?>" +
            $"((PC, OpCode, RomBank, RamBank, A, X, Y, Params, Flags, SP, CpuY, Clock) => ({predicate}) ? {expression} : null)";

        var scriptOptions = ScriptOptions.Default
            .AddReferences(typeof(Rgba32).Assembly)
            .AddImports(
                "SixLabors.ImageSharp.PixelFormats",
                "System"
            );
        //var x = new System.Func<ushort, byte, byte, byte, byte, byte, byte, ushort, byte, byte, ushort, ulong, SixLabors.ImageSharp.PixelFormats.Rgba32?>((PC, OpCode, RomBank, RamBank, A, X, Y, Params, Flags, SP, CpuY, Clock) => (OpCode == 0xea) ? new Rgba32(255, 255, 255) : null);
        // Compile the lambda
        try
        {
            var func = CSharpScript
                .EvaluateAsync<Func<ushort, byte, byte, byte, byte, byte, byte, ushort, byte, byte, ushort, ulong, Rgba32?>>(code, scriptOptions)
                .GetAwaiter().GetResult();

            return func;
        }
        catch (CompilationErrorException e)
        {
            Console.WriteLine($"Error compiling predicate: {predicate} with expression: {expression}");
            Console.WriteLine(string.Join(Environment.NewLine, e.Diagnostics));
            return (PC, OpCode, RomBank, RamBank, A, X, Y, Params, Flags, SP, CpuY, Clock) => new Rgba32(255, 0, 0);
        }
    }
}

public class CpuProfilerArguements : DebugRequestArguments
{
    public string Message { get; set; } = "";
    public List<CpuProfilerLineItem>? Items { get; set; } = null;
}

public class CpuProfilerResponse : ResponseBody
{
    public string Display { get; set; } = "";
}

public enum LineType
{
    BuiltIn,
    Colour,
    Rgb
}

public class CpuProfilerLineItem
{
    [Newtonsoft.Json.JsonProperty("predicate")]
    public string Predicate { get; set; } = "";

    [Newtonsoft.Json.JsonProperty("lineType")]
    public LineType LineType { get; set; }

    [Newtonsoft.Json.JsonProperty("definition")]
    public string Definition { get; set; } = "";

    [Newtonsoft.Json.JsonProperty("R")]
    public string R { get; set; } = "";

    [Newtonsoft.Json.JsonProperty("G")]
    public string G { get; set; } = "";

    [Newtonsoft.Json.JsonProperty("B")]
    public string B { get; set; } = "";

    public CpuProfilerLineItem()
    {
    }

    public CpuProfilerLineItem(string predicate, LineType lineType, string definition, string r, string g, string b)
    {
        Predicate = predicate;
        LineType = lineType;
        Definition = definition;
        R = r;
        G = g;
        B = b;
    }
}
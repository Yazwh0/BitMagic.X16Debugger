using BitMagic.Common;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BitMagic.X16Debugger.CustomMessage;

internal class PaletteRequest : DebugRequestWithResponse<PaletteRequestArguments, PaletteRequestResponse>
{
    public PaletteRequest() : base("bm_palette")
    {
    }
}

public class PaletteRequestArguments : DebugRequestArguments
{
}

public class PaletteRequestResponse : ResponseBody
{
    public PixelRgba[] DisplayPalette { get; set; } = Array.Empty<PixelRgba>(); 
    public VeraPaletteItem[] Palette { get; set; } = Array.Empty<VeraPaletteItem>();
}

public class VeraPaletteItem
{
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
}
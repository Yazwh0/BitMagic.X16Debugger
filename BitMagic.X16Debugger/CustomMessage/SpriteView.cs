using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using BitMagic.Common;

namespace BitMagic.X16Debugger.CustomMessage;

internal class SpriteRequest : DebugRequestWithResponse<SpriteRequestArguments, SpriteRequestResponse>
{
    public SpriteRequest() : base("spriteView")
    {
    }
}

internal static class SpriteRequestHandler
{
    private const string _getSprites = "getSprites";
    private const string _setDebugColours = "setDebugColours";

    public unsafe static SpriteRequestResponse HandleRequest(SpriteRequestArguments? arguments, Emulator emulator) =>
        arguments?.Command switch
        {
            _getSprites => GetSprites(emulator),
            _setDebugColours => SetDebugColours(arguments, emulator),
            _ => throw new NotImplementedException()
        };

    private static SpriteRequestResponse GetSprites(Emulator emulator)
    {
        var toReturn = new SpriteRequestResponse();
        toReturn.Sprites = new();

        for (var i = 0; i < 128; i++)
        {
            toReturn.Sprites.Add(new SpriteDefinition(i, emulator));
        }

        return toReturn;
    }

    public static SpriteRequestResponse SetDebugColours(SpriteRequestArguments arguments, Emulator emulator)
    {
        if (arguments.DebugSpriteColours == null)
            return new SpriteRequestResponse();

        if (arguments.DebugSpriteColours.Length != 128)
            return new SpriteRequestResponse();

        var colours = emulator.DebugSpriteColours;
        for (var i = 0; i < 128; i++)
        {
            colours[i + 1] = arguments.DebugSpriteColours[i];
        }

        return new SpriteRequestResponse();
    }
}

public class SpriteRequestArguments : DebugRequestArguments
{
    public string Command { get; set; } = string.Empty;
    public uint[]? DebugSpriteColours { get; set; } = [];
    public SpriteDefinition? Sprite { get; set; } = null;
}

public class SpriteDefinition
{
    public int Index { get; set; }
    public string Display { get; set; } = "";

    public uint Address { get; set; }
    public uint PalletteOffset { get; set; }
    public uint CollisionMask { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public uint Depth { get; set; }
    public bool HFlip { get; set; }
    public bool VFlip { get; set; }
    public uint Mode { get; set; }

    public SpriteDefinition()
    { }

    public SpriteDefinition(int index, Emulator emulator)
    {
        Index = index;

        var sprite = emulator.Sprites[index];

        Address = sprite.Address;
        PalletteOffset = sprite.PaletteOffset;
        CollisionMask = sprite.CollisionMask;
        Width = (int)sprite.Width;
        Height = (int)sprite.Height;
        X = (sprite.X & 0b10_0000_0000) == 0 ? (int)sprite.X : unchecked((int)(sprite.X | 0xfffffc00));
        Y = (sprite.Y & 0b10_0000_0000) == 0 ? (int)sprite.Y : unchecked((int)(sprite.Y | 0xfffffc00));
        Depth = sprite.Depth;
        Mode = (sprite.Mode & 0b1000000) == 0 ? 4u : 8u;

        var image = new Image<Rgba32>(Width, Height);

        var idx = (int)Address;
        for (var y = 0; y < Height; y++)
        {
            image.ProcessPixelRows(i =>
            {
                var vram = emulator.Vera.Vram;
                var palette = emulator.Palette;

                var span = i.GetRowSpan(y);

                if (Mode == 4)
                {
                    for (var x = 0; x < Width; x += 2)
                    {
                        var data = vram[idx++];
                        var v = (data & 0x0f);
                        if (v != 0)
                            span[x + 1] = ToRgba(palette[v + (int)PalletteOffset * 16]);
                        v = (data & 0xf0) >> 4;
                        if (v != 0)
                            span[x] = ToRgba(palette[v + (int)PalletteOffset * 16]);
                    }
                }
                else
                {
                    for (var x = 0; x < Width; x++)
                    {
                        var data = vram[idx++];
                        if (data != 0)
                            span[x] = ToRgba(palette[data < 16 ? data + (int)PalletteOffset * 16 : data]);
                    }
                }
            });
        }

        var memoryStream = new MemoryStream();
        image.SaveAsPng(memoryStream);
        Display = Convert.ToBase64String(memoryStream.ToArray());
    }

    private static Rgba32 ToRgba(PixelRgba source)
    {
        return new Rgba32(source.R, source.G, source.B);
    }
}

public class SpriteRequestResponse : ResponseBody
{
    public List<SpriteDefinition>? Sprites { get; set; } = null;
}
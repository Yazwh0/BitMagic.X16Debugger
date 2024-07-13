using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;

namespace BitMagic.TileCreator;

public enum TileSize
{
    Size_8,
    Size_16,
}

public enum Depth
{
    Bpp_1,
    Bpp_2,
    Bpp_4,
    Bpp_8
}


public enum TileExcessHandling
{

    Error,
    Ignore,
    Include
}


public static class TileProcessor
{
    public static X16Image LoadImage(string filename, string? transparent)
    {
        using (var image = Image.Load<Rgba32>(filename))
        {
            var toReturn = new X16Image();

            toReturn.Height = image.Height;
            toReturn.Width = image.Width;

            toReturn.Pixels = new byte[image.Width * image.Height];

            Dictionary<Rgba32, byte> palette = new();
            var paletteIndex = 0;
            var idx = 0;

            if (transparent != null)
            {
                palette.Add(Rgba32.ParseHex(transparent), (byte)paletteIndex++);
            }

            image.ProcessPixelRows(i =>
            {
                for (var y = 0; y < image.Height; y++)
                {
                    var span = i.GetRowSpan(y);
                    for (var x = 0; x < image.Width; x++)
                    {
                        var thisColour = span[x];
                        if (!palette.ContainsKey(thisColour))
                            palette.Add(thisColour, (byte)paletteIndex++);

                        toReturn.Pixels[idx++] = palette[thisColour];
                    }
                }
            });

            toReturn.Colours = new Colour[palette.Count];
            var index = 0;
            foreach (var kv in palette.OrderBy(kv => kv.Value))
            {
                toReturn.Colours[index].R = kv.Key.R;
                toReturn.Colours[index].G = kv.Key.G;
                toReturn.Colours[index].B = kv.Key.B;
                index++;
            }

            return toReturn;
        }
    }

    public static TileMapDefinition CreateTileMap(X16Image image, Depth depth, TileSize width, TileSize height, bool checkFlips = true, bool includeBlank = false, TileExcessHandling excessHandling = TileExcessHandling.Error, IEnumerable<Tile>? existing = null)
    {
        var toReturn = new TileMapDefinition();

        // cant flip 1bpp tiles.
        if (depth == Depth.Bpp_1)
        {
            checkFlips = false;
            Console.WriteLine("Warning: 1bpp tiles cannot be flipped.");
        }

        var maxColours = depth switch
        {
            Depth.Bpp_1 => 2,
            Depth.Bpp_2 => 4,
            Depth.Bpp_4 => 16,
            Depth.Bpp_8 => 256,
            _ => throw new ArgumentException($"Unknown depth {depth}")
        };

        if (image.Colours.Length > maxColours)
            throw new Exception($"There are too many colours in the source image for that depth. Image Colours: {image.Colours.Length}");

        var stepX = width switch
        {
            TileSize.Size_8 => 8,
            TileSize.Size_16 => 16,
            _ => throw new ArgumentException($"Unknown width {width}")
        };

        var stepY = height switch
        {
            TileSize.Size_8 => 8,
            TileSize.Size_16 => 16,
            _ => throw new ArgumentException($"Unknown height {width}")
        };

        if (excessHandling == TileExcessHandling.Error)
        {
            var ratio = (image.Width / (stepX * 1.0));
            if (ratio - Math.Truncate(ratio) != 0)
                throw new Exception($"image width is not divisible by {stepX}. Width: {image.Width}");

            ratio = (image.Height / (stepY * 1.0));
            if (ratio - Math.Truncate(ratio) != 0)
                throw new Exception($"image height is not divisible by {stepY}. Width: {image.Height}");
        }

        var curX = 0;
        var curY = 0;
        var comparer = new TileComparer();

        if (includeBlank)
            toReturn.Tiles.Add(new Tile(stepX, stepY, depth));

        if (existing != null)
        {
            toReturn.Tiles.AddRange(existing);
        }

        while (true)
        {
            if (excessHandling == TileExcessHandling.Ignore)
            {
                if (curX + stepX > image.Width)
                {
                    curX = 0;
                    curY += stepY;
                    continue;
                }

                if (curY + stepY > image.Height)
                {
                    break;
                }
            }

            var tile = new Tile(stepX, stepY, depth);
            for (var x = 0; x < stepX; x++)
            {
                for (var y = 0; y < stepY; y++)
                {
                    if (curX + x < image.Width && curY + y < image.Height)
                        tile.Pixels[x, y] = image.Pixels[(curY + y) * image.Width + curX + x];
                }
            }

            var index = toReturn.Tiles.FindIndex(t => comparer.Equals(t, tile));
            if (index == -1)
            {
                byte flipData = 0;
                if (checkFlips)
                {
                    var flip = tile.FlipX();

                    index = toReturn.Tiles.FindIndex(t => comparer.Equals(t, flip));

                    if (index == -1)
                    {
                        flip = tile.FlipY();

                        index = toReturn.Tiles.FindIndex(t => comparer.Equals(t, flip));

                        if (index == -1)
                        {
                            flip = flip.FlipX();

                            index = toReturn.Tiles.FindIndex(t => comparer.Equals(t, flip));

                            if (index != -1)
                            {
                                flipData = 0xc; // 1100
                            }
                        }
                        else
                        {
                            flipData = 0x4; // 100
                        }
                    }
                    else
                    {
                        flipData = 0x8; // 1000
                    }
                }

                if (index == -1)
                {
                    toReturn.Map.Add(new TileIndex((byte)toReturn.Tiles.Count, flipData));
                    toReturn.Tiles.Add(tile);
                }
                else
                {
                    toReturn.Map.Add(new TileIndex((byte)index, flipData));
                }
            }
            else
            {
                toReturn.Map.Add(new TileIndex((byte)index, (byte)0));
            }

            curX += stepX;

            if (excessHandling == TileExcessHandling.Include)
            {
                if (curX >= image.Width)
                {
                    curX = 0;
                    curY += stepY;
                    continue;
                }

                if (curY >= image.Height)
                {
                    break;
                }
            }

            if (excessHandling == TileExcessHandling.Error)
            {
                if (curX == image.Width)
                {
                    curX = 0;
                    curY += stepY;
                }

                if (curY == image.Height)
                {
                    break;
                }
            }
        }

        toReturn.Colours = image.Colours.ToArray();
        toReturn.MapWidth = (int)Math.Ceiling(image.Width / stepX * 1.0);
        toReturn.MapHeight = (int)Math.Ceiling(image.Height / stepY * 1.0);

        return toReturn;
    }
}

public class Tile
{

    public byte[,] Pixels { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public Depth Depth { get; set; }

    public Tile(int width, int height, Depth depth)
    {
        Width = width;
        Height = height;
        Pixels = new byte[width, height];
        Depth = depth;
    }

    public IEnumerable<byte> Data()
    {
        var stepX = Depth switch
        {
            Depth.Bpp_1 => 8,
            Depth.Bpp_2 => 4,
            Depth.Bpp_4 => 2,
            Depth.Bpp_8 => 1,
            _ => throw new Exception()
        };

        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x += stepX)
            {
                switch (Depth)
                {
                    case Depth.Bpp_1:
                        yield return (byte)(
                            ((Pixels[x + 0, y] & 0x01) << 7) +
                            ((Pixels[x + 1, y] & 0x01) << 6) +
                            ((Pixels[x + 2, y] & 0x01) << 5) +
                            ((Pixels[x + 3, y] & 0x01) << 4) +
                            ((Pixels[x + 4, y] & 0x01) << 3) +
                            ((Pixels[x + 5, y] & 0x01) << 2) +
                            ((Pixels[x + 6, y] & 0x01) << 1) +
                            (Pixels[x + 7, y] & 0x01));
                        break;
                    case Depth.Bpp_2:
                        yield return (byte)(
                            ((Pixels[x + 0, y] & 0x03) << 6) +
                            ((Pixels[x + 1, y] & 0x03) << 4) +
                            ((Pixels[x + 2, y] & 0x03) << 2) +
                            (Pixels[x + 3, y] & 0x03));
                        break;
                    case Depth.Bpp_4:
                        yield return (byte)(
                            ((Pixels[x + 0, y] & 0x0f) << 4) +
                            (Pixels[x + 1, y] & 0x0f));
                        break;
                    case Depth.Bpp_8:
                        yield return Pixels[x, y];
                        break;
                }
            }
        }
    }

    public Tile FlipX()
    {
        var toReturn = new Tile(Width, Height, Depth);

        for (var x = 0; x < Width; x++)
        {
            for (var y = 0; y < Height; y++)
            {
                toReturn.Pixels[x, y] = Pixels[Width - x - 1, y];
            }
        }

        return toReturn;
    }

    public Tile FlipY()
    {
        var toReturn = new Tile(Width, Height, Depth);

        for (var x = 0; x < Width; x++)
        {
            for (var y = 0; y < Height; y++)
            {
                toReturn.Pixels[x, y] = Pixels[x, Height - y - 1];
            }
        }

        return toReturn;
    }
}

public class TileComparer : IEqualityComparer<Tile>
{
    public bool Equals(Tile? x, Tile? y)
    {
        if (x == null || y == null)
            return false;

        if (x.Width != y.Width)
            return false;

        if (x.Height != y.Height)
            return false;

        for (var i = 0; i < x.Width; i++)
        {
            for (var j = 0; j < y.Height; j++)
            {
                if (x.Pixels[i, j] != y.Pixels[i, j])
                    return false;
            }
        }

        return true;
    }

    public int GetHashCode([DisallowNull] Tile obj)
    {
        throw new NotImplementedException();
    }
}


public class RawImage
{
    public byte[] Pixels { get; set; } = Array.Empty<byte>();
    public byte[] VeraColours { get; set; } = Array.Empty<byte>();
    public int Width { get; set; }
    public int Height { get; set; }
}

public class X16Image
{
    public byte[] Pixels { get; set; } = Array.Empty<byte>();
    public Colour[] Colours { get; set; } = Array.Empty<Colour>();
    public byte[] VeraColours => Colours.SelectMany(i => i.VeraColour).ToArray();
    public int Width { get; set; }
    public int Height { get; set; }
}

public class TileMapDefinition
{
    public List<Tile> Tiles { get; set; } = new List<Tile>();
    public List<TileIndex> Map { get; set; } = new List<TileIndex>();
    public int MapWidth { get; set; }
    public int MapHeight { get; set; }
    public Colour[] Colours { get; set; } = Array.Empty<Colour>();


    public IEnumerable<byte> TileData()
    {
        foreach (var i in Tiles)
        {
            foreach (var j in i.Data())
            {
                yield return j;
            }
        }
    }

    public IEnumerable<byte> TileMap()
    {
        foreach(var i in Map)
        {
            yield return i.Index;
            yield return i.FlipData;
        }
    }

    public IEnumerable<byte> Palette()
    {
        foreach(var i in Colours)
        {
            foreach (var j in i.VeraColour)
            {
                yield return j;
            }
        }
    }

}

public struct TileIndex
{
    public byte Index { get; set; } = 0;
    public byte FlipData { get; set; } = 0;

    public TileIndex()
    {
    }

    public TileIndex(byte index, byte flipData)
    {
        Index = index;
        FlipData = flipData;
    }
}


public struct Colour
{
    public byte R { get; set; } = 0;
    public byte G { get; set; } = 0;
    public byte B { get; set; } = 0;

    public Colour()
    {
    }

    public Colour(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    public IEnumerable<byte> VeraColour => new byte[] { (byte)((G & 0xf0) + ((B & 0xf0) >> 4)), (byte)((R & 0xf0) >> 4) };
}
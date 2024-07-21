using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System.Globalization;
using System.Diagnostics;

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

            Dictionary<Rgba32, int> palette = new();
            var paletteIndex = 0;
            var idx = 0;

            if (transparent != null)
            {
                palette.Add(Rgba32.ParseHex(transparent), paletteIndex++);
            }

            image.ProcessPixelRows(i =>
            {
                for (var y = 0; y < image.Height; y++)
                {
                    var span = i.GetRowSpan(y);
                    for (var x = 0; x < image.Width; x++)
                    {
                        var thisColour = ConvertToX16(span[x]);
                        if (!palette.ContainsKey(thisColour))
                            palette.Add(thisColour, paletteIndex++);

                        if (paletteIndex > 255)
                            throw new Exception("Max colours reached!");

                        toReturn.Pixels[idx++] = (byte)palette[thisColour];
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

    public static Rgba32 ConvertToX16(Rgba32 colour)
    {
        if (colour.A == 0)
            return new Rgba32(0, 0, 0, 0);

        var toReturn = new Rgba32(AdjustColour(colour.R), AdjustColour(colour.G), AdjustColour(colour.B), (byte)0xff);

        return toReturn;
    }

    private static byte AdjustColour(byte input)
    {
        byte toReturn = (byte)(input & 0xf0);

        if ((byte)(input & 0x0f) > 8)
            toReturn += 0x10;

        return (byte)(toReturn + (toReturn >> 4));
    }

    public static TilesDefinition CreateTiles(X16Image image, Depth depth, TileSize width, TileSize height, bool includeBlank = false, TileExcessHandling excessHandling = TileExcessHandling.Error, IEnumerable<Tile>? existing = null)
    {
        var toReturn = new TilesDefinition() { Depth = depth };
        var sourceIndex = 0;

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

            var tile = new Tile(stepX, stepY, depth) { SourceIndex = sourceIndex };
            toReturn.Tiles.Add(tile);

            for (var x = 0; x < stepX; x++)
            {
                for (var y = 0; y < stepY; y++)
                {
                    if (curX + x < image.Width && curY + y < image.Height)
                        tile.Pixels[x, y] = image.Pixels[(curY + y) * image.Width + curX + x];
                }
            }

            curX += stepX;
            sourceIndex++;

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

        toReturn.Colours = new List<List<Colour>>() { new List<Colour>(image.Colours) };

        for (var i = 0; i < toReturn.Tiles.Count; i++)
        {
            toReturn.Tiles[i].Index = i;
        }

        return toReturn;
    }


    // For converting an image into a tilemap
    public static TileMapDefinition CreateTileMap(X16Image image, Depth depth, TileSize width, TileSize height, bool checkFlips = true, bool includeBlank = false, TileExcessHandling excessHandling = TileExcessHandling.Error, IEnumerable<Tile>? existing = null)
    {
        var toReturn = new TileMapDefinition() { Depth = depth };
        var sourceIndex = 0;

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

            var tile = new Tile(stepX, stepY, depth) { SourceIndex = sourceIndex };

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
                    toReturn.Map.Add(new TileIndex((byte)toReturn.Tiles.Count, flipData, 0));
                    toReturn.Tiles.Add(tile);
                }
                else
                {
                    toReturn.Map.Add(new TileIndex((byte)index, flipData, 0));
                }
            }
            else
            {
                toReturn.Map.Add(new TileIndex((byte)index, (byte)0, 0));
            }

            curX += stepX;
            sourceIndex++;

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

        toReturn.Colours = new List<List<Colour>>() { new List<Colour>(image.Colours) };
        toReturn.MapWidth = (int)Math.Ceiling(image.Width / stepX * 1.0);
        toReturn.MapHeight = (int)Math.Ceiling(image.Height / stepY * 1.0);

        for (var i = 0; i < toReturn.Tiles.Count; i++)
        {
            toReturn.Tiles[i].Index = i;
        }

        return toReturn;
    }


    /// <summary>
    /// Create a TileMap based on a set of tiles and a map. The index of the map is from the original tilemap image.
    /// Unused tiles will be removed.
    /// </summary>
    /// <param name="tiles">Tile definitions from CreateTiles()</param>
    /// <param name="map">Map to be used, index is from the original tilemap image</param>
    /// <returns>TilemapDefinition</returns>
    public static TileMapDefinition CreateTileMap(TilesDefinition tiles, IEnumerable<IEnumerable<int>> map)
    {

        var width = map.Max(i => i.Count());
        var height = map.Count();

        return CreateTileMap(tiles, map, width, height);
    }

    /// <summary>
    /// Create a TileMap based on a set of tiles and a map. The index of the map is from the original tilemap image.
    /// Unused tiles will be removed.
    /// </summary>
    /// <param name="tiles">Tile definitions from CreateTiles()</param>
    /// <param name="map">Map to be used, index is from the original tilemap image</param>
    /// <returns>TilemapDefinition</returns>
    public static TileMapDefinition CreateTileMap(TilesDefinition tiles, IEnumerable<int> map, int width, int height)
    {
        return CreateTileMap(tiles, map.Batch(width), width, height);
    }

    private static IEnumerable<IEnumerable<T>> Batch<T>(this IEnumerable<T> source, int batchSize)
    {
        using (var enumerator = source.GetEnumerator())
            while (enumerator.MoveNext())
                yield return YieldBatchElements(enumerator, batchSize - 1);
    }

    private static IEnumerable<T> YieldBatchElements<T>(IEnumerator<T> source, int batchSize)
    {
        yield return source.Current;
        for (int i = 0; i < batchSize && source.MoveNext(); i++)
            yield return source.Current;
    }


    /// <summary>
    /// Create a TileMap based on a set of tiles and a map. The index of the map is from the original tilemap image.
    /// Unused tiles will be removed.
    /// </summary>
    /// <param name="tiles">Tile definitions from CreateTiles()</param>
    /// <param name="map">Map to be used, index is from the original tilemap image</param>
    /// <returns>TilemapDefinition</returns>
    public static TileMapDefinition CreateTileMap(TilesDefinition tiles, IEnumerable<IEnumerable<int>> map, int width, int height)
    {
        var toReturn = new TileMapDefinition() { Depth = tiles.Depth };

        var mapWidth = map.Max(i => i.Count());
        var mapHeight = map.Count() / mapWidth;

        if (mapWidth > width)
            throw new Exception("Map width is greater than width requested");

        if (mapHeight > height)
            throw new Exception("Map height is greater than heigh requested");

        toReturn.MapHeight = height;
        toReturn.MapWidth = width;

        toReturn.Map = new List<TileIndex>(height * width);

        var tileIndex = tiles.Tiles.ToDictionary(i => i.SourceIndex, i => i);
        var tileUsed = tiles.Tiles.ToDictionary(i => i.SourceIndex, _ => false);

        foreach (var line in map)
        {
            var count = 0;
            foreach (var i in line)
            {
                if (!tileIndex.ContainsKey(i))
                    throw new Exception($"Cannot found tile {i} in tiles");

                toReturn.Map.Add(new TileIndex(tileIndex[i].Index, 0, 0)); // as we're coming from a map image, we wont have flips

                tileUsed[i] = true;

                count++;
            }

            while (count < width)
            {
                toReturn.Map.Add(new TileIndex(0, 0, 0));
                count++;
            }
        }

        var index = 0;
        foreach (var kv in tileUsed.Where(kv => kv.Value))
        {
            tileIndex[kv.Key].Index = index++;
            toReturn.Tiles.Add(tileIndex[kv.Key]);
        }

        foreach (var i in toReturn.Map)
        {
            i.Index = tileIndex[i.Index].Index;
        }

        toReturn.Colours = new List<List<Colour>>();

        foreach (var i in tiles.Colours)
        {
            var dest = new List<Colour>(i.Count);
            toReturn.Colours.Add(dest);
            foreach (var c in i)
            {
                dest.Add(new Colour(c.R, c.G, c.B));
            }
        }

        return toReturn;
    }

    private static void ReducetoPaletteOffset(ITileCollection source, ITileCollection destination, Depth depth, int maxEntries = 16, int baseOffset = 0)
    {
        var indexPalettes = new List<List<byte>>();
        var maxColours = depth switch { Depth.Bpp_1 => 2, Depth.Bpp_2 => 4, Depth.Bpp_4 => 16, Depth.Bpp_8 => 256, _ => throw new Exception() };

        foreach (var tile in source.Tiles.Select(i => i.Clone()))
        {
            var data = tile.Data().Distinct().ToArray();   // get all data

            destination.Tiles.Add(tile);

            if (data.Count() > maxColours)
            {
                throw new Exception("Tile has too many colours");
            }

            // look to see if this index set is covered already
            var index = 0;
            var done = false;
            foreach (var p in indexPalettes)
            {
                if (p.TrueForAll(i => data.Contains(i)))
                {
                    tile.PaletteOffset = index;
                    done = true;
                    break;
                }
                index++;
            }

            if (done)
                continue;

            index = 0;
            var maxMissing = int.MaxValue;
            var maxMissingIndex = -1;
            // look for indexs that could be expanded
            foreach (var p in indexPalettes)
            {
                var missing = data.Count(i => !p.Contains(i));
                if (missing < maxMissing && missing + p.Count < 16) // look to see if this tile could be added to this palette
                {
                    maxMissing = missing;
                    maxMissingIndex = index;

                    if (missing == 0)
                        break;
                }
                index++;
            }

            if (maxMissingIndex != -1)
            {
                foreach (var i in data)
                {
                    if (!indexPalettes[maxMissingIndex].Contains(i))
                    {
                        indexPalettes[maxMissingIndex].Add(i);
                    }
                }
                tile.PaletteOffset = maxMissingIndex;
                continue;
            }

            // create a new palette
            if (indexPalettes.Count >= maxEntries)
                throw new Exception("Palette entry count exceeds maxEntries");

            indexPalettes.Add(new List<byte> { 0 }); // background
            indexPalettes[indexPalettes.Count - 1].AddRange(data.Where(i => i != 0));

            tile.PaletteOffset = indexPalettes.Count - 1;
        }

        List<Dictionary<int, int>> ColourLookup = new();
        foreach (var p in indexPalettes)
        {
            var lookup = new Dictionary<int, int>();
            ColourLookup.Add(lookup);

            var palette = new List<Colour>();
            destination.Colours.Add(palette);

            var newIndex = 0;
            foreach (var i in p)
            {
                palette.Add(source.Colours[0][i]);
                lookup.Add(i, newIndex++);
            }
        }

        // reduce the colours down and remap
        // the palette offset has been set on the tile
        foreach (var t in destination.Tiles)
        {
            for (var x = 0; x < t.Width; x++)
            {
                for (var y = 0; y < t.Height; y++)
                {
                    t.Pixels[x, y] = (byte)(ColourLookup[t.PaletteOffset][t.Pixels[x, y]]);
                }
            }
            t.Depth = depth;
        }
    }

    public static TileMapDefinition ReduceToPaletteOffset(TileMapDefinition definition, Depth depth, int maxEntries = 16, int baseOffset = 0)
    {
        if ((int)depth >= (int)definition.Depth)
            throw new Exception("Depth must be lower than the original tilemap");

        var toReturn = new TileMapDefinition() { Depth = depth, MapHeight = definition.MapHeight, MapWidth = definition.MapWidth };

        ReducetoPaletteOffset(definition, toReturn, depth, maxEntries, baseOffset);

        foreach (var m in definition.Map)
        {
            toReturn.Map.Add(new TileIndex(m.Index, m.FlipData, toReturn.Tiles[m.Index].PaletteOffset + baseOffset));
        }

        return toReturn;
    }

    public static SpriteSheet ReducetoPaletteOffset(SpriteSheet source, Depth depth, int baseOffset = 0)
    {
        var toReturn = new SpriteSheet();
        toReturn.Colours.Clear();

        ReducetoPaletteOffset(source, toReturn, depth, 16, baseOffset);

        return toReturn;
    }
}

public interface ITileCollection
{
    public List<Tile> Tiles { get; set; }
    public List<List<Colour>> Colours { get; set; }
}

public class Tile
{
    public byte[,] Pixels { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public Depth Depth { get; set; }
    public int SourceIndex { get; set; } = -1;
    public int Index { get; set; }
    public int PaletteOffset { get; set; } = 0;

    public Tile(int width, int height, Depth depth)
    {
        Width = width;
        Height = height;
        Pixels = new byte[width, height];
        Depth = depth;
    }

    public Tile Clone()
    {
        var newArr = new byte[Width, Height];
        Array.Copy(Pixels, newArr, Width * Height);

        return new Tile(Width, Height, Depth)
        {
            SourceIndex = SourceIndex,
            PaletteOffset = PaletteOffset,
            Index = Index,
            Pixels = newArr
        };
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

public class TileMapDefinition : TilesDefinition
{
    public List<TileIndex> Map { get; set; } = new List<TileIndex>();
    public int MapWidth { get; set; }
    public int MapHeight { get; set; }

    public IEnumerable<byte> TileMap()
    {
        foreach (var i in Map)
        {
            yield return (byte)i.Index;     // todo: add indexing greater than 256.
            yield return i.FlipData;
        }
    }
}

public class SpriteSheet : ITileCollection
{
    public List<Tile> Tiles { get; set; } = new List<Tile>();
    public List<List<Colour>> Colours { get; set; } = new List<List<Colour>>() { new List<Colour> { new Colour() } };

    public SpriteSheet AddSprite(X16Image image, int x, int y, int width, int height)
    {
        var toAdd = new Tile(width, height, Depth.Bpp_8); // always start with a full depth

        for (var xp = 0; xp < width; xp++)
        {
            for (var yp = 0; yp < height; yp++)
            {
                var pixel = image.Pixels[(x + xp) + (yp + y) * image.Width];

                if (pixel == 0)
                {
                    toAdd.Pixels[xp, yp] = 0;
                    continue;
                }

                var sourceColour = image.Colours[pixel];

                var newPixel = Colours[0].IndexOf(sourceColour, 1); // dont consider the background

                if (newPixel == -1)
                {
                    newPixel = Colours[0].Count;
                    Colours[0].Add(sourceColour);
                }

                toAdd.Pixels[xp, yp] = (byte)newPixel;
            }
        }

        Tiles.Add(toAdd);

        return this;
    }

    public IEnumerable<byte> SpriteData()
    {
        foreach (var i in Tiles)
        {
            foreach (var j in i.Data())
            {
                yield return j;
            }
        }
    }


    public IEnumerable<byte> Palette()
    {
        foreach (var palette in Colours)
        {
            foreach (var colour in palette) // should this write blanks?
            {
                foreach (var j in colour.VeraColour)
                {
                    yield return j;
                }
            }
        }
    }

    public IEnumerable<byte> Palette(bool fillBanks)
    {
        foreach (var palette in Colours)
        {
            foreach (var colour in palette) // should this write blanks?
            {
                foreach (var j in colour.VeraColour)
                {
                    yield return j;
                }

            }

            if (fillBanks)
            {
                for (var i = palette.Count; i < 16; i++)
                {
                    yield return 0;
                    yield return 0;
                }
            }
        }
    }

    public IEnumerable<byte> Palette(int paletteIndex, bool fillBanks)
    {
        foreach (var colour in Colours[paletteIndex]) // should this write blanks?
        {
            foreach (var j in colour.VeraColour)
            {
                yield return j;
            }
        }

        if (fillBanks)
        {
            for (var i = Colours[paletteIndex].Count; i < 16; i++)
            {
                yield return 0;
                yield return 0;
            }
        }
    }
}

public class TilesDefinition : ITileCollection
{
    public List<Tile> Tiles { get; set; } = new List<Tile>();
    public List<List<Colour>> Colours { get; set; } = new List<List<Colour>>();
    public Depth Depth { get; set; }

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

    public IEnumerable<byte> Palette()
    {
        foreach (var palette in Colours)
        {
            foreach (var colour in palette) // should this write blanks?
            {
                foreach (var j in colour.VeraColour)
                {
                    yield return j;
                }
            }
        }
    }

    public IEnumerable<byte> Palette(bool fillBanks)
    {
        foreach (var palette in Colours)
        {
            foreach (var colour in palette) // should this write blanks?
            {
                foreach (var j in colour.VeraColour)
                {
                    yield return j;
                }

            }

            if (fillBanks)
            {
                for (var i = palette.Count; i < 16; i++)
                {
                    yield return 0;
                    yield return 0;
                }
            }
        }
    }

    public IEnumerable<byte> Palette(int paletteIndex, bool fillBanks)
    {
        foreach (var colour in Colours[paletteIndex]) // should this write blanks?
        {
            foreach (var j in colour.VeraColour)
            {
                yield return j;
            }
        }

        if (fillBanks)
        {
            for (var i = Colours[paletteIndex].Count; i < 16; i++)
            {
                yield return 0;
                yield return 0;
            }
        }
    }
}

public class TileIndex
{
    public int Index { get; set; } = 0;
    public byte FlipData { get; set; } = 0;

    public TileIndex()
    {
    }

    public TileIndex(int index, byte flipData, int paletteOffset)
    {
        Index = index;
        FlipData = (byte)(((index & 0x300) >> 8) + (flipData << 2) + ((paletteOffset & 0x0f) << 4));
    }
}

[DebuggerDisplay("{Debug}")]
public struct Colour
{
    public byte R { get; set; } = 0;
    public byte G { get; set; } = 0;
    public byte B { get; set; } = 0;

    public string Debug => $"#{(R & 0xf0) >> 4:X1}{(G & 0xf0) >> 4:X1}{(B & 0xf0) >> 4:X1}";

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
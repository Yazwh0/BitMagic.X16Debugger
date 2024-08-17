using TiledCSPlus;
using BitMagic.TileCreator;

// Load Tiles project data
var basePath = "C:\\Documents\\Source\\X16Projects\\Platformer";
var tmxMap = new TiledMap(Path.Combine(basePath, "Level1.tmx"));

var tmxTileset = new TiledTileset(Path.Combine(basePath, "Platforms.tsx"));

//// Find assets location and oad
var fileName = tmxTileset.Image.Source ?? throw new Exception("Assets not found");
var image = TileProcessor.LoadImage(Path.Combine(basePath, fileName), "#00000000");

// Break assets up into tiles, doesn't reduce the tiles
var tiles = TileProcessor.CreateTiles(image, Depth.Bpp_8, TileSize.Size_16, TileSize.Size_16);

// Find layer
var layer = tmxMap.Layers.FirstOrDefault(i => i.Name == "Colission") ?? throw new Exception("Layer not found");

// Create a map from the layer using the GID as the tile index. This will only return tiles that are used.
// 0 Id means no tile, so need to substitute.
var map = TileProcessor.CreateTileMap(tiles, layer.Data.Select(i => i == 0 ? 0 : i - 1), 256, 240 / 16);

// Reduce from 8bpp to 4bpp with different pallette offsets
var reduced = TileProcessor.ReduceToPaletteOffset(map, Depth.Bpp_4);

var colours = reduced.Palette(true).ToArray();
var m = map.TileMap().ToArray();



//var basePath = "C:\\Documents\\Source\\X16Projects\\Platformer"; // todo: add a way to get to the workspace
//var image = TileProcessor.LoadImage(Path.Combine(basePath, @"Crawler/Heroes/Knight/Run", "Run-Sheet.png"), "#00000000");

//var sprites = new SpriteSheet();

//sprites.AddSprite(image, 16, 32, 16, 16);
//sprites.AddSprite(image, 16 + 64, 32, 16, 16);
//sprites.AddSprite(image, 16 + 128, 32, 16, 16);
//sprites.AddSprite(image, 16 + 192, 32, 16, 16);


//var toSave = TileProcessor.ReducetoPaletteOffset(sprites, Depth.Bpp_4);

Console.WriteLine();

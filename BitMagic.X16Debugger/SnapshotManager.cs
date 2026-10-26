using BitMagic.Common;
using BitMagic.Common.Address;
using BitMagic.X16Debugger.CustomMessage;
using BitMagic.X16Emulator;

namespace BitMagic.X16Debugger;

/// <summary>
/// Snapshots of the machine, for this session only. They're held in memory and go when the ServiceManager is reset
/// for the next launch.
/// </summary>
internal class SnapshotManager
{
    private const int ThumbnailScale = 4; // 800x525 down to 200x131

    private readonly Emulator _emulator;
    private readonly StackManager _stackManager;
    private readonly List<SnapshotEntry> _snapshots = new();
    private readonly object _lock = new();
    private int _nextId = 1;

    public SnapshotManager(Emulator emulator, StackManager stackManager)
    {
        _emulator = emulator;
        _stackManager = stackManager;
    }

    /// <summary>
    /// Only call while the emulator is stopped.
    /// </summary>
    public SnapshotEntry Take()
    {
        var saved = _emulator.SaveState();

        // the call stack works out the source line, then is regenerated when VSCode next asks for it
        _stackManager.GenerateCallStack();
        var frame = _stackManager.CallStack.FirstOrDefault()?.StackFrame;
        _stackManager.Invalidate();

        var address = AddressFunctions.GetDebuggerAddressDisplayString(_emulator.Pc, _emulator.Memory[0x00], _emulator.Memory[0x01]);
        var location = frame?.Source?.Name != null ? $" {frame.Source.Name}:{frame.Line}" : "";

        lock (_lock)
        {
            var entry = new SnapshotEntry
            {
                Id = _nextId++,
                Name = $"Frame {_emulator.Vera.Frame_Count}, PC {address}{location}",
                Created = DateTime.Now,
                Frame = _emulator.Vera.Frame_Count,
                BeamX = _emulator.Vera.Beam_X,
                BeamY = _emulator.Vera.Beam_Y,
                Thumbnail = Convert.ToBase64String(ImageFile.ToPng(Thumbnail(ScreenRequestHandler.Render(_emulator, true, new RgbaImage(800, 525))))),
                State = saved
            };

            _snapshots.Add(entry);
            return entry;
        }
    }

    public List<SnapshotEntry> Snapshots
    {
        get
        {
            lock (_lock)
                return _snapshots.ToList();
        }
    }

    public SnapshotEntry? Get(int id)
    {
        lock (_lock)
            return _snapshots.FirstOrDefault(i => i.Id == id);
    }

    public bool Delete(int id)
    {
        lock (_lock)
            return _snapshots.RemoveAll(i => i.Id == id) != 0;
    }

    public bool Rename(int id, string name)
    {
        lock (_lock)
        {
            var entry = _snapshots.FirstOrDefault(i => i.Id == id);

            if (entry == null)
                return false;

            entry.Name = name;
            return true;
        }
    }

    // box filtered, so text and sprites don't just disappear
    private static RgbaImage Thumbnail(RgbaImage source)
    {
        var toReturn = new RgbaImage(source.Width / ThumbnailScale, source.Height / ThumbnailScale);

        for (var y = 0; y < toReturn.Height; y++)
        {
            for (var x = 0; x < toReturn.Width; x++)
            {
                int r = 0, g = 0, b = 0;

                for (var sy = 0; sy < ThumbnailScale; sy++)
                {
                    for (var sx = 0; sx < ThumbnailScale; sx++)
                    {
                        var pixel = source[x * ThumbnailScale + sx, y * ThumbnailScale + sy];
                        r += pixel.R;
                        g += pixel.G;
                        b += pixel.B;
                    }
                }

                const int count = ThumbnailScale * ThumbnailScale;
                toReturn[x, y] = new PixelRgba((byte)(r / count), (byte)(g / count), (byte)(b / count));
            }
        }

        return toReturn;
    }
}

internal class SnapshotEntry
{
    public int Id { get; init; }
    public string Name { get; set; } = "";
    public DateTime Created { get; init; }
    public uint Frame { get; init; }
    public int BeamX { get; init; }
    public int BeamY { get; init; }
    public string Thumbnail { get; init; } = "";
    public SavedState State { get; init; } = null!;

    public SnapshotInfo ToInfo() => new()
    {
        Id = Id,
        Name = Name,
        Created = Created.ToString("HH:mm:ss"),
        Frame = Frame,
        BeamX = BeamX,
        BeamY = BeamY,
        Thumbnail = Thumbnail
    };
}

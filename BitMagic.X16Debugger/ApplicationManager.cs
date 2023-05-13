using Silk.NET.SDL;

namespace BitMagic.X16Debugger;

internal class EmulatorApplicationManager
{

}

public interface IEmulatorApplication
{
    string Name { get; }
    string Path { get; }
    string Filename { get; }
    string LocalFilename { get; }
    bool Loaded { get; }
    Dictionary<string, string> Documents { get; }
}

internal class EmulatorApplication : IEmulatorApplication
{
    public string Name { get; }
    public string Path { get; }
    public string Filename { get; }
    public string LocalFilename { get; }
    public bool Loaded { get; set; }
    private SourceMapManager? _sourceMapManager { get; set; } = null;
    public List<MemoryRange> MemoryRanges { get; } = new();
    public Dictionary<int, string> Symbols { get; } = new();
    public Dictionary<string, string> Documents { get; } = new();

    public EmulatorApplication(string path, string name, string filename)
    {
        Name = name;
        Path = path;
        Filename = filename;
    }
}

internal class MemoryRange
{
    public int Bank { get; set; }
    public int Start { get; set; }
    public int End { get; set; }
}
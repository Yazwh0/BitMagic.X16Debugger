using BitMagic.X16Emulator;

namespace BitMagic.X16Debugger.DebugFiles;


internal class DebugableFileManager
{
    private readonly Dictionary<string, IPrgFile> Files = new();

    public DebugableFileManager()
    {

    }

    public IPrgFile GetFile(string filename) => Files[filename];
    public void Addfile(IPrgFile file)
    {
        Files.Add(file.Filename, file);
    }
}

internal interface IPrgFile
{
    string Filename { get; }
    void LoadDebuggerInfo(Emulator emulator);
}

internal interface IPrgSourceFile
{
    string Filename { get; }
}

internal class BitMagicPrgFile : IPrgFile
{
    public string Filename { get; }
    public List<PrgSourceFile> Source { get; } = new();

    public BitMagicPrgFile(string filename)
    {
        Filename = filename;
    }

    public void LoadDebuggerInfo(Emulator emulator)
    {
    }
}

internal class PrgSourceFile : IPrgSourceFile
{
    public string Filename { get; }

    public PrgSourceFile(string filename)
    {
        Filename = filename;
    }
}
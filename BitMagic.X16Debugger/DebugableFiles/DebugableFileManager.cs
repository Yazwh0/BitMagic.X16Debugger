using BitMagic.X16Emulator;

namespace BitMagic.X16Debugger.DebugableFiles;

internal class DebugableFileManager
{
    private readonly Dictionary<string, IPrgFile> Files = new();
    private readonly Dictionary<string, IPrgSourceFile> SourceFiles = new();

    public IPrgFile? GetFile(string filename)
    {
        if (Files.ContainsKey(filename))
            return Files[filename];

        return null;
    }

    public IPrgSourceFile? GetFileFromSource(string filename)
    {
        if (SourceFiles.ContainsKey(filename))
            return SourceFiles[filename];

        return null;
    }

    public void Addfile(IPrgFile file)
    {
        Files.Add(file.Filename, file);
        foreach(var source in file.SourceFiles)
        {
            SourceFiles.Add(FixFilename(source.Filename), source);
        }
    }

    public void AddBitMagicFilesToSdCard(SdCard sdCard)
    {
        foreach (var file in Files.Values)
        {
            if (file is not BitMagicPrgFile bmPrg)
                continue;

            sdCard.AddCompiledFile(bmPrg.Filename, bmPrg.Data);
        }
    }

    private static string FixFilename(string path)
    {
#if OS_WINDOWS
        return char.ToLower(path[0]) + path[1..];
#endif
#if OS_LINUX
        return path;
#endif
    }
}

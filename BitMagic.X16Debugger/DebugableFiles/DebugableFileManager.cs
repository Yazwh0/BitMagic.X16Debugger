using BitMagic.X16Emulator;

namespace BitMagic.X16Debugger.DebugableFiles;

internal class DebugableFileManager
{
    // Keyd on prg filename
    private readonly Dictionary<string, IPrgFile> Files = new();
    // Keyd on source filename
    private readonly Dictionary<string, IPrgSourceFile> SourceFiles = new();

    public IPrgFile? GetFile(string filename)
    {
        if (Files.ContainsKey(filename))
            return Files[filename];

        return null;
    }

    public IPrgSourceFile? GetSourceFile(string filename)
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
            var filename = FixFilename(source.Filename);
            if (!SourceFiles.ContainsKey(filename))
                SourceFiles.Add(filename, source);

            //if (!SourceFiles.ContainsKey(filename))
            //    SourceFiles.Add(filename, new List<IPrgSourceFile> { source });
            //else
            //    SourceFiles[filename].Add(source);
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

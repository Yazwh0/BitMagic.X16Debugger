using BitMagic.Common;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.DebugableFiles;

internal class DebugableFileManager
{
    private readonly Dictionary<string, DebugWrapper> AllFiles = new ();

    private readonly IdManager _idManager;
    private BreakpointManager? _breakpointManager;

    internal DebugableFileManager(IdManager idManager)
    {
        _idManager = idManager;
    }

    internal void SetBreakpointManager(BreakpointManager breakpointManager)
    {
        _breakpointManager = breakpointManager;
    }

    public void ClearFiles(ISourceFile file)
    {
        var allFiles = GetAllFilesFromSourceFile(file, null);

        foreach(var i in allFiles)
            AllFiles.Remove(i);
    }

    private HashSet<string> GetAllFilesFromSourceFile(ISourceFile file, HashSet<string>? collected)
    {
        if (collected == null)
            collected = new HashSet<string>();

        if (collected.Contains(file.Path))
            return collected;

        collected.Add(file.Path);

        foreach (var p in file.Parents)
            GetAllFilesFromSourceFile(p, collected);
        foreach (var c in file.Children)
            GetAllFilesFromSourceFile(c, collected);

        return collected;
    }

    public void AddFiles(ISourceFile file)
    {
        if (AllFiles.ContainsKey(file.Path)) // important as we call this recusivley.
            return;

        var wrapper = new DebugWrapper(file, _breakpointManager ?? throw new Exception());

        if (wrapper.ReferenceId == null && !wrapper.Source.ActualFile) // do not create Ids for real files
            wrapper.ReferenceId = _idManager.AddObject(wrapper, ObjectType.DecompiledData);

        AllFiles.Add(wrapper.Path, wrapper);

        foreach (var p in file.Parents)
            AddFiles(p);

        foreach(var c in file.Children)
            AddFiles(c);
    }

    public DebugWrapper? GetFile_New(string filename)
    {
        if (AllFiles.ContainsKey(filename))
            return AllFiles[filename];

        // A file loaded by the X16 (SETNAM) uses the local path separator, whereas a binary file can be named with
        // either (eg ld65 writes 'DAT/PSM.DAT'). The SD card is FAT, so names aren't case sensitive either.
        var toFind = NormaliseX16Filename(filename);

        return AllFiles.Values.FirstOrDefault(i => i.X16File && NormaliseX16Filename(i.Path) == toFind);
    }

    /// <summary>
    /// The X16 file with this filename in any folder, eg 'kernal.bin' finds 'build/x16/kernal.bin'. Null if there
    /// isn't exactly one.
    /// </summary>
    public DebugWrapper? GetFileByName(string filename)
    {
        var toFind = NormaliseX16Filename(Path.GetFileName(filename.Replace('\\', '/')));

        var found = AllFiles.Values.Where(i => i.X16File && NormaliseX16Filename(Path.GetFileName(i.Path.Replace('\\', '/'))) == toFind).Take(2).ToArray();

        return found.Length == 1 ? found[0] : null;
    }

    private static string NormaliseX16Filename(string filename) =>
        filename.Replace('\\', '/').TrimStart('/').ToUpperInvariant();

    public IEnumerable<string> AllFilenames()
    {
        foreach(var i in AllFiles.Keys)
            yield return i;
    }

    public DebugWrapper? GetFileSource(Source source)
    {
        if (source.SourceReference != null)
        {
            var wrapper = _idManager.GetObject<DebugWrapper>(source.SourceReference.Value);

            if (wrapper != null)
                return wrapper;
        }

        return GetFile_New(source.Path);
    }

    public DebugWrapper? GetWrapper(ISourceFile sourceFile)
    {
        return AllFiles[sourceFile.Path];
        return AllFiles.Values.FirstOrDefault(i => i.Source == sourceFile);
    }

    public void AddBitMagicFilesToSdCard(SdCard sdCard, IList<string> excludes)
    {
        foreach (var i in GetBitMagicFiles())
        {
            if (!excludes.Contains(i.Filename))
                sdCard.AddCompiledFile(i.Filename, i.Data);
        }
    }

    public IEnumerable<(string Filename, byte[] Data)> GetBitMagicFiles()
    {
        foreach (var i in AllFiles.Values.Where(i => i.X16File).Select(i => i.Source).Cast<IBinaryFile>())
        {
            yield return (i.Path, i.Data.ToArray());
        }
    }
    public IEnumerable<IBinaryFile> GetBitMagicFilesToWrite() => 
        AllFiles.Values.Where(i => i.X16File).Select(i => i.Source).Cast<IBinaryFile>();
}

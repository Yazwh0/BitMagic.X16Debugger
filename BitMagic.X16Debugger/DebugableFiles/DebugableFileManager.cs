using BitMagic.Common;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.DebugableFiles;

internal class DebugableFileManager
{
    private readonly Dictionary<string, DebugWrapper> AllFiles = new ();

    // host file -> the X16 file built to it, so the project's sdCardFiles copy of it can be followed
    private readonly Dictionary<string, DebugWrapper> _hostFiles = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

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

        foreach (var i in _hostFiles.Where(i => allFiles.Contains(i.Value.Path)).Select(i => i.Key).ToArray())
            _hostFiles.Remove(i);
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

    /// <summary>
    /// The X16 file at this path on the SD card, eg '/DATA/LEVEL1.BIN', as placed by SdCardWriter.
    /// </summary>
    public DebugWrapper? GetFileOnSdCard(string sdCardPath)
    {
        var toFind = NormaliseX16Filename(sdCardPath);

        return AllFiles.Values.FirstOrDefault(i => i.X16File && i.SdCardPaths.Any(p => NormaliseX16Filename(p) == toFind));
    }

    /// <summary>
    /// Records the host file an X16 file was written to (or read from), eg 'out/LEVEL1.BIN'.
    /// </summary>
    public void SetHostFile(string path, string hostPath)
    {
        if (string.IsNullOrWhiteSpace(hostPath) || !AllFiles.TryGetValue(path, out var wrapper) || !wrapper.X16File)
            return;

        wrapper.HostPath = Path.GetFullPath(hostPath);
        _hostFiles[wrapper.HostPath] = wrapper;
    }

    /// <summary>
    /// The X16 file that was written to (or read from) this host file, if any.
    /// </summary>
    public DebugWrapper? GetFileByHostFile(string hostPath) =>
        _hostFiles.TryGetValue(Path.GetFullPath(hostPath), out var wrapper) ? wrapper : null;

    /// <summary>
    /// Every X16 file, ie the files built to go on the SD card.
    /// </summary>
    public IEnumerable<DebugWrapper> X16Files() => AllFiles.Values.Where(i => i.X16File);

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

    public IEnumerable<IBinaryFile> GetBitMagicFilesToWrite() =>
        AllFiles.Values.Where(i => i.X16File).Select(i => i.Source).Cast<IBinaryFile>();
}

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

    public void AddFiles(ISourceFile file)
    {
        if (AllFiles.ContainsKey(file.Path))
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

        return null;
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

    public void AddBitMagicFilesToSdCard(SdCard sdCard)
    {
        foreach (var i in GetBitMagicFiles())
        {
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
}

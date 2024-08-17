using BitMagic.Common;
using BitMagic.X16Debugger.Exceptions;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using System.Linq;

namespace BitMagic.X16Debugger.DebugableFiles;

internal class DebugWrapper : ISourceFile
{
    private readonly ISourceFile _sourceFile;
#if DEBUG
    private static int instanceCounter = 0;
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1144:Unused private types or members should be removed", Justification = "<Pending>")]
    private readonly int _instance = instanceCounter++;
#endif

    public bool Loaded { get; internal set; } = false;
    public int LoadedDebuggerAddress { get; internal set; }
    public List<BreakpointPair> Breakpoints { get; } = new();

    private readonly BreakpointManager _breakpointManager;
    public DebugWrapper(ISourceFile sourceFile, BreakpointManager breakpointManager)
    {
        _sourceFile = sourceFile;
        _breakpointManager = breakpointManager;
    }

    /// <summary>
    /// Called after a file is loaded into memory
    /// </summary>
    /// <param name="emulator"></param>
    /// <param name="debuggerAddress"></param>
    /// <param name="hasHeader"></param>
    /// <param name="sourceMapManager"></param>
    /// <param name="fileManager"></param>
    /// <returns></returns>
    public List<Breakpoint> FileLoaded(Emulator emulator, int debuggerAddress, bool hasHeader, SourceMapManager sourceMapManager, DebugableFileManager fileManager)
    {
        Loaded = true;

        var file = _sourceFile as IBinaryFile;

        if (file == null)
            throw new DebugWrapperFileNotBinaryException(_sourceFile);

        LoadedDebuggerAddress = debuggerAddress;

        sourceMapManager.ClearSourceMap(debuggerAddress, file.Data.Count - (hasHeader ? 2 : 0)); // remove old sourcemap
        // this clears all the debug data
        _breakpointManager.ClearBreakpoints(debuggerAddress, file.Data.Count - (hasHeader ? 2 : 0)); // Unload breakpoints that we're overwriting

        sourceMapManager.ConstructNewSourceMap(file, hasHeader);

        var breakpoints = _breakpointManager.CreateBitMagicBreakpoints(debuggerAddress, this, fileManager); // set breakpoints as verified (loade)

        file.LoadDebugData(emulator, sourceMapManager, debuggerAddress);

        return breakpoints;
    }

    internal IEnumerable<BreakpointPair> FindParentBreakpoints(int lineNumber, DebugableFileManager fileManager)
    {
        foreach (var bps in Breakpoints)
        {
            if (bps.SourceBreakpoint.Line == lineNumber + 1) // VSC line numbers are not 0 based
                yield return bps;
        }

        if (Parents.Count == 0) // all done
            yield break;

        // look at parents
        if (ParentMap.Count <= lineNumber)
            yield break;

        var parentMap = ParentMap[lineNumber];

        if (parentMap.relativeId != -1)
        {
            var wrapper = fileManager.GetWrapper(Parents[parentMap.relativeId]);

            if (wrapper == null)
                yield break;

            foreach (var b in wrapper.FindParentBreakpoints(parentMap.relativeLineNumber, fileManager))
                yield return b;
        }
    }

    internal IEnumerable<(int DebuggerAddress, bool Loaded)> FindUltimateAddresses(int lineNumber, DebugableFileManager fileManager)
    {
        if (Children.Count == 0)
        {
            var binary = Source as IBinaryFile;

            if (binary == null)
                yield break;

            yield return (LoadedDebuggerAddress + lineNumber, Loaded);

            yield break;
        }

        foreach (var cl in ChildrenMap.Where(i => i.contentLineNumber == lineNumber))
        {
            var child = fileManager.GetWrapper(Children[cl.relativeId]);

            if (child == null)
                yield break;

            foreach (var debuggerAddress in child.FindUltimateAddresses(cl.relativeLineNumber, fileManager))
                yield return debuggerAddress;
        }
    }

    internal (ISourceFile? SourceFile, int lineNumber) FindUltimateSource(int lineNumber, DebugableFileManager fileManager)
    {
        if (lineNumber > ParentMap.Count)
            return (Source, lineNumber);

        if (ParentMap[lineNumber].relativeId == -1)
        {
            return (Source, lineNumber);
        }

        var wrapper = fileManager.GetWrapper(Parents[ParentMap[lineNumber].relativeId]);

        if (wrapper == null)
            return (null, -1);

        return wrapper.FindUltimateSource(ParentMap[lineNumber].relativeLineNumber, fileManager);
    }

    public ISourceFile Source => _sourceFile;

    #region ISourceFile
    public string Name => _sourceFile.Name;

    public string Path => _sourceFile.Path;

    public int? ReferenceId { get => _sourceFile.ReferenceId; set => _sourceFile.ReferenceId = value; }

    public SourceFileType Origin => _sourceFile.Origin;

    public bool RequireUpdate => _sourceFile.RequireUpdate;

    public bool ActualFile => _sourceFile.ActualFile;
    public bool X16File => _sourceFile.X16File;

    public IReadOnlyList<ISourceFile> Parents => _sourceFile.Parents;

    public IReadOnlyList<ISourceFile> Children => _sourceFile.Children;

    public IReadOnlyList<string> Content => _sourceFile.Content;

    public IReadOnlyList<ParentSourceMapReference> ParentMap => _sourceFile.ParentMap;

    public IReadOnlyList<ChildSourceMapReference> ChildrenMap => _sourceFile.ChildrenMap;

    public Task UpdateContent() => _sourceFile.UpdateContent();

    public void MapChildren() => _sourceFile.MapChildren();

    public void AddChild(ISourceFile child) => _sourceFile.AddChild(child);

    public int AddParent(ISourceFile parent)
    {
        throw new NotImplementedException();
    }

    public void SetParentMap(int lineNumber, int parentLineNumber, int parentId)
    {
        throw new NotImplementedException();
    }
    #endregion
}

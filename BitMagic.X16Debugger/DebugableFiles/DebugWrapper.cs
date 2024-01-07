using BitMagic.Common;
using BitMagic.Common.Address;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.DebugableFiles;

internal class DebugWrapper : ISourceFile
{
    private readonly ISourceFile _sourceFile;
#if DEBUG
    private static int instanceCounter = 0;
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
    /// Loads the file into main memory at _address_.
    /// Will load into the bank that is set.
    /// </summary>
    /// <param name="emulator"></param>
    /// <param name="address">Actual address to load into.</param>
    /// <param name="hasHeader">Are we including the header or not?</param>
    /// <param name="sourceMapManager"></param>
    /// <exception cref="DebugWrapperAlreadyLoadedException"></exception>
    /// <exception cref="DebugWrapperFileNotBinaryException"></exception>
    [Obsolete]
    public List<Breakpoint> Load(Emulator emulator, int address, bool hasHeader, SourceMapManager sourceMapManager, DebugableFileManager fileManager)
    {
        if (Loaded)
            throw new DebugWrapperAlreadyLoadedException(this);

        var file = _sourceFile as IBinaryFile;

        if (file == null)
            throw new DebugWrapperFileNotBinaryException(_sourceFile);

        file.LoadIntoMemory(emulator, address);

        Loaded = true;
        LoadedDebuggerAddress = address;

        var debuggerAddress = AddressFunctions.GetDebuggerAddress(address, emulator);

        sourceMapManager.ClearSourceMap(debuggerAddress, file.Data.Count - (hasHeader ? 2 : 0)); // remove old sourcemap
        _breakpointManager.ClearBreakpoints(debuggerAddress, file.Data.Count - (hasHeader ? 2 : 0)); // Unload breakpoints that we're overwriting

        sourceMapManager.ConstructNewSourceMap(file, hasHeader);

        var breakpoints = _breakpointManager.CreateBitMagicBreakpoints(debuggerAddress, this, fileManager); // set breakpoints as verified (loade)

        if (file is BitMagicBinaryFile bitmagicFile)
        {
            bitmagicFile.MapProcToMemory(emulator, sourceMapManager); // map bitmagic lines to the sourecmap for the stack
        }

        return breakpoints;
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
        _breakpointManager.ClearBreakpoints(debuggerAddress, file.Data.Count - (hasHeader ? 2 : 0)); // Unload breakpoints that we're overwriting

        sourceMapManager.ConstructNewSourceMap(file, hasHeader);

        var breakpoints = _breakpointManager.CreateBitMagicBreakpoints(debuggerAddress, this, fileManager); // set breakpoints as verified (loade)

        if (file is BitMagicBinaryFile bitmagicFile)
        {
            bitmagicFile.MapProcToMemory(emulator, sourceMapManager); // map bitmagic lines to the sourecmap for the stack
        }

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

    // This needs to call into the breakpoint manager
    //public List<Breakpoint> SetBreakpoints(SetBreakpointsArguments arguments, Emulator emulator, HashSet<int> debuggerBreakpoints, DebugableFileManager fileManager, IdManager idManager)
    //{
    //    var toReturn = new List<Breakpoint>();

    //    _breakpointManager.ClearBreakpoints(this);

    //    //foreach (var bp in Breakpoints)
    //    //{
    //    //    if (bp.PrimaryAddress != 0)
    //    //        emulator.Breakpoints[bp.PrimaryAddress] &= 0x80;
    //    //    if (bp.SecondaryAddress != 0)
    //    //        emulator.Breakpoints[bp.SecondaryAddress] &= 0x80;
    //    //}

    //    Breakpoints.Clear();

    //    // need to work from the source file (this) down the children to the address in memory
    //    foreach (var sbp in arguments.Breakpoints)
    //    {
    //        var added = false;
    //        foreach (var (debuggerAddress, loaded) in FindUltimateAddresses(sbp.Line - 1, fileManager))
    //        {
    //            if (!loaded)
    //                continue;

    //            var breakpoint = sbp.ConvertBreakpoint(arguments.Source, loaded, idManager);

    //            // set system bit
    //            var breakpointValue = debuggerBreakpoints.Contains(debuggerAddress) ? (byte)0x81 : (byte)0x01;

    //            var (_, bank) = AddressFunctions.GetAddressBank(debuggerAddress);

    //            var (address, secondAddress) = AddressFunctions.GetMemoryLocations(debuggerAddress);
    //            var currentBank = address >= 0xc000 ? emulator.RomBankAct : emulator.RamBankAct;

    //            if (address < 0xa000 || bank == currentBank)
    //                emulator.Breakpoints[address] = breakpointValue;

    //            if (secondAddress != 0)
    //                emulator.Breakpoints[secondAddress] = breakpointValue;

    //            added = true;
    //            Breakpoints.Add(new BreakpointPair(breakpoint, sbp, address, secondAddress));
    //            toReturn.Add(breakpoint);
    //        }

    //        if (!added)
    //        {
    //            var breakpoint = sbp.ConvertBreakpoint(arguments.Source, false, idManager);
    //            Breakpoints.Add(new BreakpointPair(breakpoint, sbp, 0, 0));
    //            toReturn.Add(breakpoint);
    //        }
    //    }

    //    return toReturn;
    //}

    internal IEnumerable<(int DebuggerAddress, bool Loaded)> FindUltimateAddresses(int lineNumber, DebugableFileManager fileManager)
    {
        if (Children.Count == 0)
        {
            var binary = Source as IBinaryFile;

            if (binary == null)
                yield break;

            yield return (LoadedDebuggerAddress + lineNumber, Loaded);// (binary.BaseAddress + lineNumber, Loaded);

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

    public IReadOnlyList<ISourceFile> Parents => _sourceFile.Parents;

    public IReadOnlyList<ISourceFile> Children => _sourceFile.Children;

    public IReadOnlyList<string> Content => _sourceFile.Content;

    public IReadOnlyList<ParentSourceMapReference> ParentMap => _sourceFile.ParentMap;

    public IReadOnlyList<ChildSourceMapReference> ChildrenMap => _sourceFile.ChildrenMap;

    public Task UpdateContent() => _sourceFile.UpdateContent();

    public void MapChildren() => _sourceFile.MapChildren();
    #endregion
}

internal class BreakpointPair
{
    public Breakpoint Breakpoint { get; init; }
    public SourceBreakpoint SourceBreakpoint { get; init; }
    public int PrimaryAddress { get; set; }
    public int SecondaryAddress { get; set; }

    public BreakpointPair(Breakpoint breakpoint, SourceBreakpoint sourceBreakpoint, int primaryAddress, int secondaryAddress)
    {
        Breakpoint = breakpoint;
        SourceBreakpoint = sourceBreakpoint;
        PrimaryAddress = primaryAddress;
        SecondaryAddress = secondaryAddress;
    }

}

internal class DebugWrapperAlreadyLoadedException : Exception
{
    public DebugWrapper Wrapper { get; }

    public DebugWrapperAlreadyLoadedException(DebugWrapper wrapper) : base("Wrapper already loaded.")
    {
        Wrapper = wrapper;
    }
}

internal class DebugWrapperFileNotBinaryException : Exception
{
    public ISourceFile File { get; }
    public DebugWrapperFileNotBinaryException(ISourceFile file) : base("File is not IBinaryFile")
    {
        File = file;
    }

}
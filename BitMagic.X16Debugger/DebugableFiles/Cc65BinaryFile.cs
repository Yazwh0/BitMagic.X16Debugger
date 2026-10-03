using BitMagic.Cc65Lib;
using BitMagic.Common;
using BitMagic.Common.Address;
using BitMagic.Compiler;
using BitMagic.Compiler.Files;
using BitMagic.X16Emulator;
using Microsoft.Extensions.FileSystemGlobbing;
using System.IO.Enumeration;

namespace BitMagic.X16Debugger.DebugableFiles;

internal static class Cc65BinaryFileFactory
{
    public static CompileState BuildAndAdd(Cc65InputFile inputFile, ServiceManager serviceManager, string basePath, IEmulatorLogger logger)
    {
        var (files, state) = Build(inputFile, basePath, logger);

        foreach (var file in files)
        {
            serviceManager.DebugableFileManager.AddFiles(file);
        }

        return state;
    }

    /// <summary>
    /// Creates a Cc65BinaryFile for each output file in the ld65 debug file, with the source map taken from the
    /// line -> span -> segment records. Each byte is mapped by its offset in the output file, so the debugger can
    /// relocate it to wherever (and whichever bank) it is loaded.
    /// </summary>
    internal static (List<Cc65BinaryFile> Files, CompileState State) Build(Cc65InputFile inputFile, string basePath, IEmulatorLogger logger)
    {
        basePath = Path.GetFullPath(Path.Combine(basePath, ToLocalPath(inputFile.BasePath)));

        if (string.IsNullOrWhiteSpace(inputFile.DebugFile))
            throw new Exception("cc65 files require a 'debugFile', created by ld65 using '--dbgfile'.");

        var debugFilename = Path.Combine(basePath, ToLocalPath(inputFile.DebugFile));
        if (!File.Exists(debugFilename))
            throw new Exception($"Cannot find cc65 debug file '{debugFilename}'.");

        if (inputFile.Outputs.Length == 0)
            throw new Exception("cc65 files require at least one 'outputs' entry.");

        logger.LogLine($"Building cc65 source map from '{inputFile.DebugFile}'");

        var debugInfo = DebugFileParser.ParseFile(debugFilename);

        if (debugInfo.Spans.Count == 0)
            logger.LogError($"  '{inputFile.DebugFile}' has no line information. Assemble with '-g' (for cl65 it must come before the source files).");

        var (state, scopes) = Cc65CompileState.Build(debugInfo, inputFile.DebugFile);

        var objects = LoadObjectFiles(inputFile, basePath, logger);
        var sourceFiles = new SourceFileResolver(inputFile, basePath, logger);
        var toReturn = new List<Cc65BinaryFile>();
        var matchedOutputs = new HashSet<Cc65InputFileOutput>();

        foreach (var outputSegments in debugInfo.OutputFiles)
        {
            var outputName = outputSegments.Key;
            var output = FindOutput(inputFile, outputName);

            if (output == null)
            {
                logger.LogLine($"  Skipping '{outputName}', not in 'outputs'.");
                continue;
            }

            matchedOutputs.Add(output);

            var referenceFile = string.IsNullOrWhiteSpace(output.ReferenceFile) ? "" : Path.Combine(basePath, ToLocalPath(output.ReferenceFile));
            if (!File.Exists(referenceFile))
                referenceFile = Path.Combine(basePath, ToLocalPath(outputName));

            if (!File.Exists(referenceFile))
            {
                logger.LogError($"Cannot find file '{referenceFile}'.");
                continue;
            }

            var data = File.ReadAllBytes(referenceFile);
            var headerSize = output.HasHeader ? 2 : 0;

            var startAddress = output.StartAddress != 0 ? output.StartAddress : GetStartAddress(outputSegments, headerSize);
            var binaryFile = new Cc65BinaryFile(outputName, AddressFunctions.GetDebuggerAddress(startAddress, 0, 0), data.Length);
            binaryFile.Data = data;
            binaryFile.State = state;

            var mapped = MapLines(debugInfo, outputName, data.Length - headerSize, headerSize, binaryFile, sourceFiles);
            MapScopes(scopes, outputName, headerSize, binaryFile);

            if (objects.Count != 0)
                VerifyObjects(objects, debugInfo, outputName, data, logger);

            logger.LogLine($"  '{outputName}' added to debugable files. 0x{startAddress:X4} -> 0x{startAddress + data.Length - headerSize - 1:X4}, {mapped} bytes mapped to source.");
            toReturn.Add(binaryFile);
        }

        foreach (var output in inputFile.Outputs.Where(i => !matchedOutputs.Contains(i)))
        {
            logger.LogLine($"  Warning: Output '{output.Filename}' is not in the debug file.");
        }

        // source files can be shared between output files, so only map once all children are added.
        foreach (var binaryFile in toReturn)
        {
            foreach (var p in binaryFile.Parents)
                p.AddChild(binaryFile);
        }

        foreach (var binaryFile in toReturn)
        {
            foreach (var p in binaryFile.Parents)
                p.MapChildren();
        }

        logger.LogLine("... Done.");

        return (toReturn, state);
    }

    private static Cc65InputFileOutput? FindOutput(Cc65InputFile inputFile, string outputName)
    {
        var name = ToMatchPath(outputName);

        return inputFile.Outputs.FirstOrDefault(i => ToMatchPath(i.Filename) == name) ??
            inputFile.Outputs.FirstOrDefault(i => FileSystemName.MatchesSimpleExpression(ToMatchPath(i.Filename), name));
    }

    /// <summary>
    /// The run address of the first byte after the header.
    /// </summary>
    private static int GetStartAddress(IEnumerable<DebugSegment> segments, int headerSize)
    {
        var first = segments.Where(i => i.Size > 0 && i.Offset >= headerSize).MinBy(i => i.Offset);

        if (first == null)
            return 0;

        return first.Start - (first.Offset - headerSize);
    }

    /// <summary>
    /// Sets the parent map for every byte that has a line. Where spans overlap the outer line wins over a macro body,
    /// and a smaller span wins over a larger one.
    /// </summary>
    private static int MapLines(DebugInfo debugInfo, string outputName, int length, int headerSize, Cc65BinaryFile binaryFile, SourceFileResolver sourceFiles)
    {
        var mapped = new bool[length];
        var count = 0;

        var lineSpans = debugInfo.Lines
            .Where(i => i.SourceFile != null)
            .SelectMany(line => line.Spans
                .Where(span => span.Segment != null && span.Segment.OutputFile == outputName)
                .Select(span => (Line: line, Span: span)))
            .OrderBy(i => LinePriority(i.Line.Type))
            .ThenByDescending(i => i.Span.Size);

        foreach (var (line, span) in lineSpans)
        {
            var parentId = sourceFiles.GetParentId(line.SourceFile!, binaryFile);

            if (parentId == -1)
                continue;

            var offset = span.Segment!.Offset + span.Start - headerSize;

            for (var i = 0; i < span.Size; i++)
            {
                var index = offset + i;
                if (index < 0 || index >= length)
                    continue;

                binaryFile.SetParentMap(index, line.LineNumber - 1, parentId);

                if (!mapped[index])
                {
                    mapped[index] = true;
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Sets the innermost scope for each byte, so locals and stack frame names work.
    /// </summary>
    private static void MapScopes(Dictionary<DebugScope, Procedure> scopes, string outputName, int headerSize, Cc65BinaryFile binaryFile)
    {
        var scopeSpans = scopes
            .SelectMany(scope => scope.Key.Spans
                .Where(span => span.Segment != null && span.Segment.OutputFile == outputName)
                .Select(span => (Procedure: scope.Value, Span: span)))
            .OrderByDescending(i => i.Span.Size);

        foreach (var (procedure, span) in scopeSpans)
        {
            var offset = span.Segment!.Offset + span.Start - headerSize;

            for (var i = 0; i < span.Size; i++)
                binaryFile.SetScope(offset + i, procedure);
        }
    }

    private static int LinePriority(DebugLineType type) => type switch
    {
        DebugLineType.Macro => 0,
        DebugLineType.Assembler => 1,
        _ => 2
    };

    private static List<Cc65Obj> LoadObjectFiles(Cc65InputFile inputFile, string basePath, IEmulatorLogger logger)
    {
        var objects = new List<Cc65Obj>();

        foreach (var i in inputFile.ObjectFiles)
        {
            var matcher = new Matcher();
            matcher.AddInclude(i.Replace('\\', '/'));

            var foundItem = false;
            foreach (var f in matcher.GetResultsInFullPath(basePath))
            {
                logger.LogLine($"  Loading object file '{f}'");
                objects.Add(Cc65LibParser.Parse(f));
                foundItem = true;
            }

            if (!foundItem)
                logger.LogLine($"  Warning: No files found for '{i}'.");
        }

        return objects;
    }

    /// <summary>
    /// Checks the literal bytes in each object file against the output file, using the module's top level scope to
    /// find where each of its segments was linked.
    /// </summary>
    private static void VerifyObjects(List<Cc65Obj> objects, DebugInfo debugInfo, string outputName, byte[] data, IEmulatorLogger logger)
    {
        foreach (var obj in objects)
        {
            var moduleName = Path.GetFileName(obj.Filename);
            var module = debugInfo.Modules.FirstOrDefault(i => i.LibraryId == -1 && string.Equals(i.Name, moduleName, StringComparison.OrdinalIgnoreCase));

            if (module == null)
            {
                logger.LogLine($"  Warning: Object file '{moduleName}' is not in the debug file.");
                continue;
            }

            if (module.RootScope == null)
                continue; // no -g, so no spans to check against.

            foreach (var segment in obj.Segments)
            {
                if (segment.Size == 0)
                    continue;

                var segmentName = obj.StringPool[(int)segment.Name_stringId];
                var span = module.RootScope.Spans.FirstOrDefault(i => i.Segment != null && i.Segment.Name == segmentName && i.Segment.OutputFile == outputName);

                if (span == null)
                    continue;

                var offset = span.Segment!.Offset + span.Start;
                var fragmentIndex = 0;

                foreach (var fragment in segment.Fragments)
                {
                    if ((fragment.FragmentType & Fragment.TypeMask) == Fragment.Literal)
                    {
                        for (var i = 0; i < fragment.Data.Length; i++)
                        {
                            var index = offset + i;
                            if (index >= data.Length || data[index] != fragment.Data[i])
                            {
                                logger.LogError($"  '{moduleName}' does not match '{outputName}' in segment {segmentName} fragment {fragmentIndex} at 0x{index:X4}. Is the build out of date?");
                                return;
                            }
                        }
                    }

                    offset += fragment.Size();
                    fragmentIndex++;
                }
            }
        }
    }

    /// <summary>
    /// ld65 writes '/' as the separator (and libraries can be mixed), project files tend to use '\'.
    /// </summary>
    internal static string ToLocalPath(string path) =>
        path.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar);

    private static string ToMatchPath(string path) =>
        path.Replace('\\', '/').TrimStart('.', '/');

    /// <summary>
    /// Finds the source files on disk, and adds them as parents to the binary files. The same parent is used for
    /// every binary file, as files are tracked by path.
    /// </summary>
    private sealed class SourceFileResolver
    {
        private readonly Cc65InputFile _inputFile;
        private readonly string _basePath;
        private readonly IEmulatorLogger _logger;
        private readonly List<string> _externalSourceFiles;
        private readonly Dictionary<int, StaticTextFile?> _files = new();
        private readonly Dictionary<(Cc65BinaryFile, int), int> _parentIds = new();

        public SourceFileResolver(Cc65InputFile inputFile, string basePath, IEmulatorLogger logger)
        {
            _inputFile = inputFile;
            _basePath = basePath;
            _logger = logger;
            _externalSourceFiles = inputFile.Includes
                .Where(i => Path.GetExtension(i).ToLower() is not ".lib" and not ".o")
                .Select(i => Path.Combine(basePath, ToLocalPath(i)))
                .ToList();
        }

        public int GetParentId(DebugSourceFile sourceFile, Cc65BinaryFile binaryFile)
        {
            if (_parentIds.TryGetValue((binaryFile, sourceFile.FileId), out var parentId))
                return parentId;

            var file = GetFile(sourceFile);
            parentId = file == null ? -1 : binaryFile.AddParent(file);

            _parentIds.Add((binaryFile, sourceFile.FileId), parentId);

            return parentId;
        }

        private StaticTextFile? GetFile(DebugSourceFile sourceFile)
        {
            if (_files.TryGetValue(sourceFile.FileId, out var file))
                return file;

            var filename = FindFile(sourceFile.Name);

            if (filename == null)
            {
                _logger.LogError($"Cannot find source file '{sourceFile.Name}'.");
            }
            else
            {
                CheckModified(sourceFile, filename);
                file = new StaticTextFile(File.ReadAllText(filename), filename, true);
            }

            _files.Add(sourceFile.FileId, file);

            return file;
        }

        private string? FindFile(string name)
        {
            name = name.Replace('\\', '/');

            foreach (var m in _inputFile.Filemap)
            {
                var path = m.Path.Replace('\\', '/');
                if (path.Length != 0 && name.StartsWith(path))
                    name = m.Replace.Replace('\\', '/') + name[path.Length..];
            }

            name = ToLocalPath(name);

            var candidates = Path.IsPathRooted(name)
                ? [name]
                : new[] { Path.Combine(_basePath, name), Path.Combine(_basePath, ToLocalPath(_inputFile.SourcePath), name) };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate).FixFilename();
            }

            var filename = Path.GetFileName(name);
            var external = _externalSourceFiles.FirstOrDefault(i => Path.GetFileName(i) == filename && File.Exists(i));

            return external == null ? null : Path.GetFullPath(external).FixFilename();
        }

        private void CheckModified(DebugSourceFile sourceFile, string filename)
        {
            var info = new System.IO.FileInfo(filename);
            var modified = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();

            if ((sourceFile.Size != 0 && info.Length != sourceFile.Size) || (sourceFile.ModifiedTime != 0 && modified > sourceFile.ModifiedTime + 2))
                _logger.LogLine($"  Warning: '{filename}' has changed since it was assembled, source lines may not match.");
        }
    }
}

internal class Cc65BinaryFile : SourceFileBase, ICompiledBinaryFile
{
    public Cc65BinaryFile(string name, int baseAddress, int size)
    {
        BaseAddress = baseAddress;
        Path = name;
        Name = System.IO.Path.GetFileName(name);
        _parentMap = new ParentSourceMapReference[size];
        _scopeMap = new IScope?[size];
        for (var i = 0; i < _parentMap.Length; i++)
        {
            _parentMap[i] = new ParentSourceMapReference(-1, -1);
        }
    }

    public override bool X16File => true;

    private IReadOnlyList<string> _content = Array.Empty<string>();
    public override IReadOnlyList<string> Content { get => _content; protected set => _content = value; }

    private List<ISourceFile> _parents { get; set; } = new List<ISourceFile>();
    public override IReadOnlyList<ISourceFile> Parents => _parents;

    private ParentSourceMapReference[] _parentMap { get; }
    public override IReadOnlyList<ParentSourceMapReference> ParentMap => _parentMap;

    private readonly Dictionary<int, string> _symbols = new();
    public IReadOnlyDictionary<int, string> Symbols => _symbols;

    public int BaseAddress { get; private set; }

    internal byte[] Data { get; set; } = Array.Empty<byte>();
    IReadOnlyList<byte> IBinaryFile.Data => Data;

    public bool Written { get; private set; }

    public CompileState State { get; internal set; } = null!; // set by Cc65BinaryFileFactory

    private readonly IScope?[] _scopeMap;

    internal void SetScope(int index, IScope scope)
    {
        if (index >= 0 && index < _scopeMap.Length)
            _scopeMap[index] = scope;
    }

    public void LoadDebugData(Emulator emulator, SourceMapManager sourceMapManager, int debuggerAddress)
    {
        for (int i = 0; i < _parentMap.Length; i++)
        {
            if (_parentMap[i].relativeId == -1)
            {
                debuggerAddress = AddressFunctions.IncrementDebuggerAddress(debuggerAddress);
                continue;
            }

            sourceMapManager.AddSourceMap(debuggerAddress, new TextLine(new SourceFilePosition()
            {
                LineNumber = i - 1,
                Name = Name,
                Source = "",
                SourceFile = this
            }, true, _scopeMap[i]));

            debuggerAddress = AddressFunctions.IncrementDebuggerAddress(debuggerAddress);
        }
    }

    public override Task UpdateContent() => Task.CompletedTask;

    public override int AddParent(ISourceFile parent)
    {
        _parents.Add(parent);
        return _parents.Count - 1;
    }

    public override void SetParentMap(int lineNumber, int parentLineNumber, int parentId)
    {
        _parentMap[lineNumber] = new ParentSourceMapReference(parentLineNumber, parentId);
    }

    public void Relocate(int newBaseAddress)
    {
        BaseAddress = newBaseAddress;
    }

    public void SetWritten()
    {
        Written = true;
    }
}

using BitMagic.Cc65Lib;
using BitMagic.Common;
using BitMagic.Common.Address;
using BitMagic.Compiler;
using BitMagic.Compiler.Files;
using BitMagic.X16Emulator;

namespace BitMagic.X16Debugger.DebugableFiles;

internal static class Cc65BinaryFileFactory
{
    public static void BuildAndAdd(Cc65InputFile inputFile, ServiceManager serviceManager, string basePath)
    {
        var cc65Cfg = Cc65CfgParser.Parse(Path.Combine(basePath, inputFile.Config), Path.Combine(basePath, inputFile.Filename), inputFile.StartAddress);

        var cc65obj = Cc65LibParser.Parse(Path.Combine(basePath, inputFile.ObjectFile), Path.Combine(basePath, inputFile.SourcePath));

        var includes = new List<Cc65Obj>();
        var libraries = new List<Cc65Library>();
        foreach(var i in inputFile.Includes)
        {
            if (Path.GetExtension(i) == ".lib")
            {
                libraries.Add(Cc65LibParser.ParseLib(Path.Combine(basePath, i)));
            }
            else
            {
                includes.Add(Cc65LibParser.Parse(Path.Combine(basePath, i)));
            }
        }

        foreach(var file in cc65Cfg.Files.Values)
        {
            var actualData = File.ReadAllBytes(Path.Combine(basePath, file.Filename));

            var toAdd = new Cc65BinaryFile(file.Filename, file.StartAddress ?? inputFile.StartAddress, actualData.Length);
            var sourceMap = new Dictionary<uint, int>();

            var currentAddress = inputFile.StartAddress;
            var adjust = 0;

            foreach(var area in file.Areas)
            {
                // get rid of any file header
                if (area.Value.StartAddress != null && area.Value.StartAddress < toAdd.BaseAddress)
                {
                    adjust += toAdd.BaseAddress - area.Value.StartAddress.Value;
                    continue;
                }

                foreach (var s in area.Value.Segments)
                {
                    Cc65Lib.Segment? segment = null;

                    var exp = cc65obj.StringPool.IndexOf(s.Key);

                    foreach (var i in cc65obj.Segments)
                    {
                        var toCheck = cc65obj.StringPool[(int)i.Name_stringId];
                        if (toCheck == s.Key)
                        {
                            segment = i;
                            break;
                        }
                    }

                    if(segment == null)
                    {
                        foreach(var l in libraries)
                        {
                            if (l.Index.ContainsKey(s.Key.ToLower() + ".o"))
                            {
                                var obj = l.Index[s.Key.ToLower() + ".o"];
                                if (obj.Cc65Obj == null)
                                    continue;

                                foreach (var i in obj.Cc65Obj.Segments)
                                {
                                    var toCheck = obj.Cc65Obj.StringPool[(int)i.Name_stringId];
                                    if (toCheck == s.Key)
                                    {
                                        segment = i;
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (segment == null)
                    {
                        if (s.Value.Optional)
                            continue;
                        else
                            throw new Exception($"Cannot find segment {s.Key}");
                    }

                    if (s.Value.StartAddress != null)
                        currentAddress = s.Value.StartAddress.Value;

                    if (s.Key == "EXEHDR")
                    {
                        currentAddress += (int)segment.Size;
                        continue;
                    }

                    var fragIdx = 0;
                    foreach (var i in segment.Fragments)
                    {
                        if (i.IsExpression)
                        {
                            // we dont evaluate expressions, or map code
                            fragIdx++;
                            currentAddress += i.Size();
                            continue;
                        }

                        // map code
                        if ((i.FragmentType & Fragment.TypeMask) == Fragment.Literal)
                        {
                            var lineIdx = i.LineInfo[i.LineInfo.Count - 1];
                            var lineInfo = cc65obj.Lines[(int)lineIdx];
                            var sourceFile = cc65obj.Files[(int)lineInfo.FileInfo_id];
                            //var sourceFilename = cc65obj.StringPool[(int)sourceFile.Filename_StringId];

                            if (!sourceMap.ContainsKey(lineInfo.FileInfo_id))
                            {
                                var sourceFilename = Path.GetFullPath(Path.Combine(basePath, cc65obj.StringPool[(int)sourceFile.Filename_StringId])).FixFilename();
                                var idx = toAdd.AddParent(new StaticTextFile(File.ReadAllText(sourceFilename), sourceFilename, true));
                                sourceMap.Add(lineInfo.FileInfo_id, idx);
                            }

                            toAdd.SetParentMap(currentAddress - inputFile.StartAddress, (int)lineInfo.Line - 1, sourceMap[lineInfo.FileInfo_id]);
                        }

                        for (var j = 0; j < i.Data.Length; j++)
                        {
                            var idx = currentAddress - inputFile.StartAddress + adjust;
                            if (i.Data[j] != actualData[idx])
                            {
                                throw new Exception($"Difference between object file and generated file at {idx} in segment {s.Key} fragment {fragIdx}");
                            }
                            currentAddress++;
                        }
                        fragIdx++;
                    }
                }
            }

            toAdd.Data = actualData;

            foreach(var p in toAdd.Parents)
            {
                p.AddChild(toAdd);
                p.MapChildren();
            }
            serviceManager.DebugableFileManager.AddFiles(toAdd);
        }
    }
}

internal class Cc65BinaryFile : SourceFileBase, IBinaryFile
{
    public Cc65BinaryFile(string name, int baseAddress, int size)
    {
        BaseAddress = baseAddress;
        Path = name;
        Name = name;
        _parentMap = new ParentSourceMapReference[size];
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

    public void LoadDebugData(Emulator emulator, SourceMapManager sourceMapManager, int debuggerAddress)
    {
        for (int i = 0; i < _parentMap.Length; i++)
        {
            if (_parentMap[i].relativeId == -1)
                continue;

            var debuggerAddressAct = AddressFunctions.GetDebuggerAddress(debuggerAddress + i, emulator);

            sourceMapManager.AddSourceMap(debuggerAddressAct, new TextLine(new SourceFilePosition()
            {
                 LineNumber = i - 1,
                 Name = Name,
                 Source = "",
                 SourceFile = this
            }, true));
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
}

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
    public static void BuildAndAdd(Cc65InputFile inputFile, ServiceManager serviceManager, string basePath, IEmulatorLogger logger)
    {
        logger.LogLine($"Building cc65 object map for config '{inputFile.Config}'");

        basePath = Path.Combine(basePath, inputFile.BasePath);

        var defaultFile = inputFile.Outputs.FirstOrDefault(i => i.Default) ?? inputFile.Outputs.FirstOrDefault();

        if (defaultFile == null)
        {
            throw new Exception("No default output defined");
        }

        var cc65Cfg = Cc65CfgParser.Parse(Path.Combine(basePath, inputFile.Config), inputFile.DefaultOuputFile, Path.Combine(basePath, defaultFile.Filename), defaultFile.StartAddress);

        var objects = new List<Cc65Obj>();

        foreach (var i in inputFile.ObjectFiles)
        {
            var matcher = new Matcher();
            matcher.AddInclude(i);

            var foundItem = false;
            foreach (var f in matcher.GetResultsInFullPath(basePath))
            {
                logger.LogLine($"  Loading object file {Path.Combine(inputFile.SourcePath, f)}");
                objects.Add(Cc65LibParser.Parse(f, Path.Combine(basePath, inputFile.SourcePath)));
                foundItem = true;
            }
            if (!foundItem)
            {
                logger.LogLine($"  Warning: No files found for '{i}'.");
            }
        }

        var includes = new List<Cc65Obj>();
        var libraries = new List<Cc65Library>();
        var externalSourceFiles = new List<string>();

        foreach (var i in inputFile.Includes)

        {
            switch (Path.GetExtension(i).ToLower())
                {
                case ".lib":
                    libraries.Add(Cc65LibParser.ParseLib(Path.Combine(basePath, i)));
                    break;
                case ".mac":
                    externalSourceFiles.Add(Path.Combine(basePath, i));
                    break;
                default:
                    includes.Add(Cc65LibParser.Parse(Path.Combine(basePath, i)));
                    break;
            }
        }

        foreach (var file in cc65Cfg.Files.Values)
        {
            // look for exact first
            var thisFile = inputFile.Outputs.FirstOrDefault(i => i.Filename == file.Filename);

            if (thisFile == null)
            {
                thisFile = inputFile.Outputs.FirstOrDefault(i => FileSystemName.MatchesSimpleExpression(i.Filename, file.Filename));
            }

            if (thisFile == null)
            {
                logger.LogLine($"Skipping {file.Filename}");
                continue;
            }

            var referenceFile = Path.Combine(basePath, thisFile.ReferenceFile);
            if (!File.Exists(referenceFile))
            {
                referenceFile = Path.Combine(basePath, thisFile.Filename);
            }

            byte[] actualData;
            if (!File.Exists(referenceFile))
            {
                logger.LogError($"Cannot find file '{thisFile.Filename}'.");
                continue;
            }
            else
            {
                actualData = File.ReadAllBytes(referenceFile);
            }

            var firstBank = file.Areas.Min(i => i.Value.Bank) ?? 0;
            var startAddress = (file.StartAddress ?? 0) == 0 ? thisFile.StartAddress : (file.StartAddress ?? 0);
            var toAdd = new Cc65BinaryFile(file.Filename, AddressFunctions.GetDebuggerAddress(startAddress, firstBank, firstBank), actualData.Length);

            var currentAddress = startAddress; // thisFile.StartAddress;
            var adjust = thisFile.HasHeader ? 2 : 0;
            var currentBank = firstBank;

            var sourceMap = new Dictionary<string, int>();

            foreach (var area in file.Areas)
            {
                // ignore file header
                if (area.Value.StartAddress != null && (area.Value.StartAddress & 0xffff) < (toAdd.BaseAddress & 0xffff))
                {
                    //adjust += toAdd.BaseAddress - area.Value.StartAddress.Value;
                    continue;
                }

                if (area.Value.StartAddress.HasValue && area.Value.StartAddress != 0)
                {
                    if (area.Value.StartAddress.Value < currentAddress && currentAddress != 0)
                    {
                        currentBank = (area.Value.Bank.HasValue && area.Value.Bank.Value != 0) ? area.Value.Bank.Value : currentBank++;
                        adjust += area.Value.StartAddress.Value >= 0xa000 && area.Value.StartAddress.Value < 0xc000 ? 0x2000 : 0x4000; // add on ram\rom bank
                    }
                    currentAddress = area.Value.StartAddress.Value;
                }

                var startCount = currentAddress;

                foreach (var s in area.Value.Segments)
                {
                    Cc65Lib.Segment? segment = null;

                    var possibles = new List<Cc65Obj>();
                    var possibleSegments = new List<Cc65Lib.Segment>();

                    foreach (var ofile in objects)
                    {
                        foreach (var i in ofile.Segments)
                        {
                            var toCheck = ofile.StringPool[(int)i.Name_stringId];
                            if (toCheck == s.Key)
                            {
                                possibles.Add(ofile);
                                possibleSegments.Add(i);
                            }
                        }
                    }

                    if (possibles.Count == 0)
                    {
                        foreach (var l in libraries)
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

                        if (segment == null)
                        {
                            if (s.Value.Optional)
                                continue;
                            else
                            {
                                logger.LogLine($"Warning: Cannot find segment {s.Key}");
                                //throw new Exception($"Cannot find segment {s.Key}");
                                continue;
                            }
                        }
                    }

                    if (s.Value.Align != null && s.Value.Align > 1)
                    {
                        if (currentAddress % s.Value.Align != 0)
                        {
                            currentAddress = ((currentAddress / s.Value.Align.Value) + 1) * s.Value.Align.Value;
                        }
                    }

                    // Can there be multiple possible segments?! Is this correct???

                    for (var segmentIndex = 0; segmentIndex < possibles.Count; segmentIndex++)
                    {
                        segment = possibleSegments[segmentIndex];

                        var cc65obj = possibles[segmentIndex];
                        //var sourceMap = sourceMaps[cc65obj.Filename];

                        //                        Console.WriteLine($"{cc65obj.StringPool[(int)segment.Name_stringId]} Start address: ${currentAddress:X4} {currentAddress}, Size {segment.Size} CfgSize {s.Value.si}");


                        if (s.Value.StartAddress != null)
                        {
                            currentAddress = s.Value.StartAddress.Value;
                        }


                        if (s.Key == "EXEHDR")
                        {
                            currentAddress += (int)segment.Size;
                            continue;
                        }

                        var fragIdx = 0;
                        foreach (var i in segment.Fragments)
                        {
                            // map code
                            var lineIdx = i.LineInfo[i.LineInfo.Count - 1];
                            var lineInfo = cc65obj.Lines[(int)lineIdx];
                            var sourceFile = cc65obj.Files[(int)lineInfo.FileInfo_id];
                            var fl = cc65obj.StringPool[(int)sourceFile.Filename_StringId];
                            //Console.WriteLine($"{file.Filename} {area.Value.Name} {cc65obj.StringPool[(int)sourceFile.Filename_StringId]}\t{(int)lineInfo.Line - 1}");


                            if (i.IsExpression)
                            {
                                //Console.WriteLine($"Expression of 0x{i.Size():X2}");
                                // we dont evaluate expressions, or map code
                                fragIdx++;
                                currentAddress += i.Size();
                                continue;
                            }

                            if ((i.FragmentType & Fragment.TypeMask) == Fragment.Literal)
                            {
                                if (!sourceMap.ContainsKey(fl))
                                {
                                    string sourceFilename = fl;

                                    foreach (var m in inputFile.Filemap)
                                    {
                                        if (!sourceFilename.StartsWith(m.Path))
                                            continue;

                                        sourceFilename = sourceFilename.Replace(m.Path, m.Replace);
                                    }

                                    sourceFilename = Path.Combine(basePath, sourceFilename);
                                    sourceFilename = Path.GetFullPath(sourceFilename);
                                    sourceFilename = sourceFilename.FixFilename();

                                    if (!File.Exists(sourceFilename))
                                    {
                                        foreach (var f in externalSourceFiles)
                                        {
                                            if (Path.GetFileName(f) == Path.GetFileName(sourceFilename))
                                            {
                                                sourceFilename = f;
                                                break;
                                            }
                                        }

                                        if (!File.Exists(sourceFilename))
                                        {
                                            logger.LogError($"Cannot find file '{sourceFilename}'");
                                        }
                                    }

                                    var idx = toAdd.AddParent(new StaticTextFile(File.ReadAllText(sourceFilename), sourceFilename, true));
                                    sourceMap.Add(fl, idx);
                                }

                                //Console.WriteLine($"{file.Filename} {area.Value.Name} {cc65obj.StringPool[(int)sourceFile.Filename_StringId]}\t{(int)lineInfo.Line - 1}");

                                var offset = currentAddress - startAddress + (currentBank - firstBank) * 0x2000;
                                toAdd.SetParentMap(offset, (int)lineInfo.Line - 1, sourceMap[fl]);
                            }

                            for (var j = 0; j < i.Data.Length; j++)
                            {
                                var idx = currentAddress - startAddress + adjust;
                                if (i.Data[j] != actualData[idx] && i.FragmentType != 0x20)
                                {
                                    Console.WriteLine($"Missmatch Data {idx:X4} ({startAddress+idx:X4}) : {i.Data[j]:X2} Act: {actualData[idx]:X2}");
                                    throw new Exception($"Difference between object file and generated file at {idx} in segment {s.Key} fragment {fragIdx}");
                                }
                                currentAddress++;
                            }
                            fragIdx++;
                        }
                    }
                }

                if (area.Value.Size != 0 && area.Value.Size > 0)
                {
                    //Console.WriteLine($"{currentAddress - startCount} Area : {area.Value.Size}");
                    currentAddress = startCount + (int)area.Value.Size;
                }
            }

            toAdd.Data = actualData;

            foreach (var p in toAdd.Parents)
            {
                p.AddChild(toAdd);
            }

            foreach (var p in toAdd.Parents)
            {
                p.MapChildren();
            }

            logger.LogLine($"  '{toAdd.Name}' added to debugable files. 0x{startAddress:X4} -> 0x{currentAddress-1:X4}");
            serviceManager.DebugableFileManager.AddFiles(toAdd);
        }

        logger.LogLine("... Done.");
    }
}

internal class Cc65BinaryFile : SourceFileBase, IBinaryFile
{
    public Cc65BinaryFile(string name, int baseAddress, int size)
    {
        BaseAddress = baseAddress;
        Path = name;
        Name = System.IO.Path.GetFileName(name);
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
            }, true));

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
}

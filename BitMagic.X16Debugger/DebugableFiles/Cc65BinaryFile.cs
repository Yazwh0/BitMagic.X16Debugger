﻿using BitMagic.Cc65Lib;
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
        basePath = Path.Combine(basePath, inputFile.BasePath);

        var defaultFile = inputFile.Outputs.FirstOrDefault(i => i.Default) ?? inputFile.Outputs.FirstOrDefault();

        if (defaultFile == null)
        {
            throw new Exception("No default output defined");
        }

        var cc65Cfg = Cc65CfgParser.Parse(Path.Combine(basePath, inputFile.Config), Path.Combine(basePath, defaultFile.Filename), defaultFile.StartAddress);

        var objects = new List<Cc65Obj>();
        var allSourceMaps = new Dictionary<string, Dictionary<uint, int>>();

        var objfilename = Path.Combine(basePath, inputFile.ObjectFile);
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(objfilename), Path.GetFileName(objfilename)))
        {
            objects.Add(Cc65LibParser.Parse(f, Path.Combine(basePath, inputFile.SourcePath)));
        }
        //var cc65obj = Cc65LibParser.Parse(Path.Combine(basePath, inputFile.ObjectFile), Path.Combine(basePath, inputFile.SourcePath));

        var includes = new List<Cc65Obj>();
        var libraries = new List<Cc65Library>();
        foreach (var i in inputFile.Includes)

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

        foreach (var file in cc65Cfg.Files.Values)
        {
            Cc65InputFileOutput? thisFile = inputFile.Outputs.FirstOrDefault(i => i.Filename == file.Filename);

            if (thisFile == null)
            {
                Console.WriteLine($"Skipping {file.Filename}");
                continue;
            }

            var actualData = File.ReadAllBytes(Path.Combine(basePath, file.Filename));

            var toAdd = new Cc65BinaryFile(file.Filename, (file.StartAddress ?? 0) == 0 ? thisFile.StartAddress : (file.StartAddress ?? 0), actualData.Length);

            var currentAddress = thisFile.StartAddress;
            var adjust = thisFile.HasHeader ? 2 : 0;

            foreach (var area in file.Areas)
            {
                // get rid of any file header
                if (area.Value.StartAddress != null && area.Value.StartAddress < toAdd.BaseAddress)
                {
                    //adjust += toAdd.BaseAddress - area.Value.StartAddress.Value;
                    continue;
                }

                if (area.Value.StartAddress != 0)
                {
                    currentAddress = area.Value.StartAddress.Value;
                }
                var startCount = currentAddress;

                //Console.WriteLine(area.Value.Name);

                foreach (var s in area.Value.Segments)
                {
                    Cc65Lib.Segment? segment = null;

                    //var exp = cc65obj.StringPool.IndexOf(s.Key);

                    var possibles = new List<Cc65Obj>();
                    var possibleSegments = new List<Cc65Lib.Segment>();
                    var sourceMaps = new Dictionary<string, Dictionary<uint, int>>();
                    foreach (var ofile in objects)
                    {
                        foreach (var i in ofile.Segments)
                        {
                            var toCheck = ofile.StringPool[(int)i.Name_stringId];
                            if (toCheck == s.Key)
                            {
                                possibles.Add(ofile);
                                possibleSegments.Add(i);
                                if (!allSourceMaps.ContainsKey(ofile.Filename))
                                    allSourceMaps.Add(ofile.Filename, new Dictionary<uint, int>());

                                sourceMaps.Add(ofile.Filename, allSourceMaps[ofile.Filename]);
                                break;
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
                                throw new Exception($"Cannot find segment {s.Key}");
                        }
                    }

                    for (var segmentIndex = 0; segmentIndex < possibles.Count; segmentIndex++)
                    {
                        segment = possibleSegments[segmentIndex];

                        var cc65obj = possibles[segmentIndex];
                        var sourceMap = sourceMaps[cc65obj.Filename];

                        //                        Console.WriteLine($"{cc65obj.StringPool[(int)segment.Name_stringId]} Start address: ${currentAddress:X4} {currentAddress}, Size {segment.Size} CfgSize {s.Value.si}");


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
                                //lastFragment = i;
                                //lastSize = lastFragment.Size();
                                //var lineIdx = i.LineInfo[i.LineInfo.Count - 1];
                                //var lineInfo = cc65obj.Lines[(int)lineIdx];
                                //var sourceFile = cc65obj.Files[(int)lineInfo.FileInfo_id];
                                //Console.WriteLine($"{cc65obj.StringPool[(int)sourceFile.Filename_StringId]}\t{(int)lineInfo.Line - 1}\t{lastSize}");
                                continue;
                            }

                            // map code
                            if ((i.FragmentType & Fragment.TypeMask) == Fragment.Literal)
                            {
                                var lineIdx = i.LineInfo[i.LineInfo.Count - 1];
                                var lineInfo = cc65obj.Lines[(int)lineIdx];
                                var sourceFile = cc65obj.Files[(int)lineInfo.FileInfo_id];

                                if (!sourceMap.ContainsKey(lineInfo.FileInfo_id))
                                {
                                    string sourceFilename = "";
                                    var fl = cc65obj.StringPool[(int)sourceFile.Filename_StringId];

                                    foreach (var m in inputFile.Filemap)
                                    {
                                        if (!fl.StartsWith(m.Path))
                                            continue;

                                        fl = fl.Replace(m.Path, m.Replace);
                                    }

                                    var f = Path.Combine(basePath, fl);
                                    sourceFilename = Path.GetFullPath(f);
                                    sourceFilename = sourceFilename.FixFilename();

                                    var idx = toAdd.AddParent(new StaticTextFile(File.ReadAllText(sourceFilename), sourceFilename, true));
                                    sourceMap.Add(lineInfo.FileInfo_id, idx);
                                }

                                //Console.WriteLine($"{cc65obj.StringPool[(int)sourceFile.Filename_StringId]}\t{(int)lineInfo.Line - 1}");
                                toAdd.SetParentMap(currentAddress - thisFile.StartAddress, (int)lineInfo.Line - 1, sourceMap[lineInfo.FileInfo_id]);
                            }

                            for (var j = 0; j < i.Data.Length; j++)
                            {
                                var idx = currentAddress - thisFile.StartAddress + adjust;
                                if (i.Data[j] != actualData[idx] && i.FragmentType != 0x20)
                                {
                                    Console.WriteLine($"Missmatch Data : {i.Data[j]:X2} Act: {actualData[idx]:X2}");
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

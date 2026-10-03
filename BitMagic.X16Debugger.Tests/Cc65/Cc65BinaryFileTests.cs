using BitMagic.Cc65Lib;
using BitMagic.Common;
using BitMagic.X16Debugger.DebugableFiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BitMagic.X16Debugger.Tests.Cc65;

// TestData is built from its own folder with:
//   ca65 -g --cpu 65c02 -t cx16 ./src/main.asm
//   ca65 -g --cpu 65c02 -t cx16 ./src/second.asm
//   ld65 -o BUILD.PRG -C test.cfg --dbgfile build.dbg --lib c64.lib ./src/main.o ./src/second.o
[TestClass]
public class Cc65BinaryFileTests
{
    private static string TestData => Path.Combine(AppContext.BaseDirectory, "Cc65", "TestData");

    private static Cc65InputFile CreateInputFile(params string[] objectFiles) => new()
    {
        Type = "cc65",
        DebugFile = "build.dbg",
        ObjectFiles = objectFiles,
        Outputs =
        [
            new Cc65InputFileOutput { Filename = "BUILD.PRG", HasHeader = true, Default = true },
            new Cc65InputFileOutput { Filename = "DAT\\*", HasHeader = false }
        ]
    };

    private static (string Path, int Line) GetSource(Cc65BinaryFile file, int index)
    {
        var map = file.ParentMap[index];
        Assert.AreNotEqual(-1, map.relativeId, $"Index {index} is not mapped.");

        return (Path.GetFileName(file.Parents[map.relativeId].Path), map.relativeLineNumber);
    }

    [TestMethod]
    public void GetPairs_QuotedValuesKeepCommasAndEquals()
    {
        var pairs = DebugFileParser.GetPairs("id=1,name=\"a,b=c.s\",size=10");

        Assert.AreEqual("1", pairs["id"]);
        Assert.AreEqual("a,b=c.s", pairs["name"]);
        Assert.AreEqual("10", pairs["size"]);
    }

    [TestMethod]
    public void Parse_LinkWithMultipleSpans()
    {
        var info = DebugFileParser.Parse([
            "version\tmajor=2,minor=0",
            "file\tid=0,name=\"main.asm\",size=10,mtime=0x6AC0C42C,mod=0",
            "line\tid=0,file=0,line=5,type=2,count=1,span=0+1",
            "mod\tid=0,name=\"main.o\",file=0",
            "seg\tid=0,name=\"CODE\",start=0x000801,size=0x0010,addrsize=absolute,type=ro,oname=\"BUILD.PRG\",ooffs=2",
            "span\tid=0,seg=0,start=0,size=2",
            "span\tid=1,seg=0,start=4,size=2",
            "scope\tid=0,name=\"\",mod=0,size=6,span=0+1"
        ]);

        var line = info.Lines.Single();
        Assert.AreEqual(DebugLineType.Macro, line.Type);
        Assert.AreEqual("main.asm", line.SourceFile!.Name);
        CollectionAssert.AreEqual(new[] { 0, 1 }, line.Spans.Select(i => i.SpanId).ToArray());
        Assert.AreEqual(0x801, line.Spans[0].Segment!.Start);
        Assert.AreEqual(2, line.Spans[0].Segment!.Offset);
        Assert.AreEqual(0x6AC0C42C, info.SourceFiles[0].ModifiedTime);
        Assert.AreEqual(2, info.Modules[0].RootScope!.Spans.Count);
    }

    [TestMethod]
    public void Build_MapsPrgToSource()
    {
        var logger = new RecordingLogger();
        var files = Cc65BinaryFileFactory.Build(CreateInputFile(), TestData, logger).Files;

        CollectionAssert.AreEqual(Array.Empty<string>(), logger.Errors);

        var prg = files.Single(i => i.Name == "BUILD.PRG");
        Assert.AreEqual(0x801, prg.BaseAddress);

        Assert.AreEqual(("main.asm", 9), GetSource(prg, 0));        // .byte $0C, $08
        Assert.AreEqual(("main.asm", 24), GetSource(prg, 0x818 - 0x801)); // lda #3
        Assert.AreEqual(("main.asm", 28), GetSource(prg, 0x81e - 0x801)); // inc
        Assert.AreEqual(("second.asm", 4), GetSource(prg, 0x822 - 0x801)); // ldx #$42, from the second module
    }

    [TestMethod]
    public void Build_MacroMapsToInvocation()
    {
        var files = Cc65BinaryFileFactory.Build(CreateInputFile(), TestData, new RecordingLogger()).Files;
        var prg = files.Single(i => i.Name == "BUILD.PRG");

        for (var address = 0x81a; address < 0x81e; address++)
        {
            Assert.AreEqual(("main.asm", 25), GetSource(prg, address - 0x801)); // macroexample
        }
    }

    [TestMethod]
    public void Build_WildcardOutputInFolder()
    {
        var files = Cc65BinaryFileFactory.Build(CreateInputFile(), TestData, new RecordingLogger()).Files;

        var bank = files.Single(i => i.Name == "BANK.BIN");
        Assert.AreEqual("DAT/BANK.BIN", bank.Path);
        Assert.AreEqual(0xa000, bank.BaseAddress);
        Assert.AreEqual(("second.asm", 9), GetSource(bank, 0)); // ldy #$10
        Assert.AreEqual(("second.asm", 10), GetSource(bank, 2)); // rts
    }

    [TestMethod]
    public void Build_SourceFilesSharedBetweenOutputs()
    {
        var files = Cc65BinaryFileFactory.Build(CreateInputFile(), TestData, new RecordingLogger()).Files;

        var prgSecond = files.Single(i => i.Name == "BUILD.PRG").Parents.Single(i => i.Path.EndsWith("second.asm"));
        var bankSecond = files.Single(i => i.Name == "BANK.BIN").Parents.Single(i => i.Path.EndsWith("second.asm"));

        Assert.AreSame(prgSecond, bankSecond);
        Assert.AreEqual(2, prgSecond.Children.Count);
    }

    [TestMethod]
    public void Build_ObjectFilesMatch()
    {
        var logger = new RecordingLogger();
        Cc65BinaryFileFactory.Build(CreateInputFile("src/*.o"), TestData, logger);

        CollectionAssert.AreEqual(Array.Empty<string>(), logger.Errors);
    }

    [TestMethod]
    public void Build_ObjectFilesMismatchIsReported()
    {
        var folder = Path.Combine(Path.GetTempPath(), "BitMagicCc65Test" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyFolder(TestData, folder);

            var prgFilename = Path.Combine(folder, "BUILD.PRG");
            var data = File.ReadAllBytes(prgFilename);
            data[0x822 - 0x801 + 2] = 0xea; // ldx -> nop, in second.o
            File.WriteAllBytes(prgFilename, data);

            var logger = new RecordingLogger();
            Cc65BinaryFileFactory.Build(CreateInputFile("src/*.o"), folder, logger);

            Assert.AreEqual(1, logger.Errors.Count);
            StringAssert.Contains(logger.Errors[0], "second.o");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [TestMethod]
    public void Build_MissingDebugFileThrows()
    {
        var inputFile = CreateInputFile();
        inputFile.DebugFile = "missing.dbg";

        Assert.ThrowsException<Exception>(() => Cc65BinaryFileFactory.Build(inputFile, TestData, new RecordingLogger()));
    }

    private static void CopyFolder(string source, string destination)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private class RecordingLogger : IEmulatorLogger
    {
        public List<string> Errors { get; } = [];

        public void Log(string message) { }
        public void LogLine(string message) { }
        public void LogError(string message) => Errors.Add(message);
        public void LogError(string message, ISourceFile source, int lineNumber) => Errors.Add(message);
    }
}

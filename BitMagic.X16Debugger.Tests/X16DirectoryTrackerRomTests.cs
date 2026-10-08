using BitMagic.Compiler.Files;
using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.Tests.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BitMagic.X16Debugger.Tests;

/// <summary>
/// Boots the real ROM, runs a program that does a 'CD:DAT' then a relative LOAD, and checks the debugger follows the
/// DOS into the folder. Needs the BITMAGIC_ROM environment variable, set to a rom.bin or the folder holding one.
/// </summary>
[TestClass]
public class X16DirectoryTrackerRomTests
{
    private const int SetNam = 0xffbd;
    private const int SetLfs = 0xffba;
    private const int Open = 0xffc0;
    private const int Close = 0xffc3;
    private const int Load = 0xffd5;

    private const int LoadAddress = 0x9000;

    private static readonly byte[] RootFile = [0x11, 0x22, 0x33, 0x44];
    private static readonly byte[] DatFile = [0xa9, 0x01, 0x60, 0xea]; // lda #1, rts, nop

    // builtAs 'DAT/TEST.BIN': built straight into the folder.
    // builtAs 'TEST.BIN': built to the root of the output folder and copied into DAT by the project's sdCardFiles.
    [TestMethod]
    [DataRow("DAT/TEST.BIN")]
    [DataRow("TEST.BIN")]
    public void CdThenLoad_ResolvesToFolder_AndBreakpointVerifies(string builtAs)
    {
        var rom = FindRom();
        if (rom == null)
        {
            Assert.Inconclusive("Set BITMAGIC_ROM to a rom.bin, or the folder holding one, to run this test.");
            return;
        }

        var emulator = new Emulator();
        var serviceManager = new ServiceManager(_ => emulator, new FakeEmulatorLogger());
        var outputFolder = Path.Combine(Path.GetTempPath(), $"cdtest-{Guid.NewGuid():N}");

        try
        {
            var romData = File.ReadAllBytes(rom);
            for (var i = 0; i < romData.Length; i++)
                emulator.RomBank[i] = romData[i];

            var fat32Symbols = Path.Combine(Path.GetDirectoryName(rom)!, "fat32.sym");
            if (File.Exists(fat32Symbols))
                serviceManager.DirectoryTracker.FindCurVolume([new SymbolsFile { Symbols = fat32Symbols, RomBank = 3 }]);

            // a binary with one line of source, as a cc65 project gives
            var source = new StaticTextFile("    lda #1\n    rts\n", "c:\\test\\cdtest.s", true);
            var binary = new Cc65BinaryFile(builtAs, LoadAddress, DatFile.Length);
            var parentId = binary.AddParent(source);
            binary.SetParentMap(0, 0, parentId);
            binary.SetParentMap(1, 0, parentId);
            binary.SetParentMap(2, 1, parentId);
            binary.Data = DatFile;
            source.AddChild(binary);
            source.MapChildren();

            var files = serviceManager.DebugableFileManager;
            files.AddFiles(binary);

            // the build writes it to the output folder
            var hostFile = Path.Combine(outputFolder, builtAs);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(hostFile)!);
            File.WriteAllBytes(hostFile, DatFile);
            files.SetHostFile(builtAs, hostFile);

            var sdCard = new SdCard(16, new FakeEmulatorLogger());
            emulator.LoadSdCard(sdCard);

            // the test's own files: a different TEST.BIN in the root, and the program that does the CD and LOAD
            sdCard.AddCompiledFile("TEST.BIN", RootFile);
            sdCard.AddCompiledFile("CDTEST.PRG", BuildProgram());

            // then the debugger places everything, as at launch
            SdCardFile[] sdCardFiles = builtAs.Contains('/') ? [] : [new SdCardFile { Source = hostFile, Dest = "DAT" }];
            new SdCardWriter(sdCard, files, new FakeEmulatorLogger()).Write(sdCardFiles, outputFolder, [], "CDTEST.PRG");

            Run(emulator);

            // the DOS loaded the file in DAT, not the root
            CollectionAssert.AreEqual(DatFile, emulator.Memory.Slice(LoadAddress, DatFile.Length).ToArray(), "LOAD didn't load DAT/TEST.BIN, so the CD failed.");

            var tracker = serviceManager.DirectoryTracker;
            Assert.AreEqual("/DAT", tracker.Refresh());

            var path = tracker.Resolve("TEST.BIN");
            Assert.AreEqual("/DAT/TEST.BIN", path);

            var breakpoint = new SetBreakpointsArguments
            {
                Source = new Source { Path = source.Path },
                Breakpoints = [new SourceBreakpoint(2)]
            };

            Assert.IsFalse(serviceManager.BreakpointManager.HandleSetBreakpointsRequest(breakpoint).Breakpoints.Single().Verified, "Not loaded yet.");

            // what the LOAD hook does with the resolved SETNAM
            var loaded = files.GetFileOnSdCard(path);
            Assert.IsNotNull(loaded, $"No debug info found for '{path}'.");
            loaded.FileLoaded(emulator, LoadAddress, false, serviceManager.SourceMapManager, files);

            Assert.IsTrue(serviceManager.BreakpointManager.HandleSetBreakpointsRequest(breakpoint).Breakpoints.Single().Verified);
        }
        finally
        {
            emulator.Dispose();

            if (System.IO.Directory.Exists(outputFolder))
                System.IO.Directory.Delete(outputFolder, true);
        }
    }

    private static string? FindRom()
    {
        var rom = Environment.GetEnvironmentVariable("BITMAGIC_ROM");
        if (string.IsNullOrWhiteSpace(rom))
            return null;

        if (System.IO.Directory.Exists(rom))
            rom = Path.Combine(rom, "rom.bin");

        return File.Exists(rom) ? rom : null;
    }

    private static void Run(Emulator emulator)
    {
        emulator.Pc = (ushort)((emulator.RomBank[0x3ffd] << 8) + emulator.RomBank[0x3ffc]);

        // stop rather than hang if the program never gets to its stp
        using var timeout = new Timer(_ => emulator.Control = Control.Stop, null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);

        var result = emulator.Emulate();

        Assert.AreEqual(Emulator.EmulatorResult.DebugOpCode, result, $"Program didn't reach its stp, PC is ${emulator.Pc:X4}.");
    }

    /// <summary>
    /// 10 SYS2061, then: OPEN 15,8,15,"CD:DAT" : CLOSE 15 : LOAD "TEST.BIN",8,2 to $9000 : stp
    /// </summary>
    private static byte[] BuildProgram()
    {
        const int codeStart = 0x80d;

        var code = new List<byte>();
        var fixups = new List<(int Index, string Name)>();

        void Bytes(params byte[] b) => code.AddRange(b);
        void Jsr(int address) => Bytes(0x20, (byte)(address & 0xff), (byte)(address >> 8));
        void Name(string name)
        {
            Bytes(0xa9, (byte)name.Length);     // lda #len
            fixups.Add((code.Count + 1, name));
            Bytes(0xa2, 0, 0xa0, 0);            // ldx #<name, ldy #>name
            Jsr(SetNam);
        }

        Name("CD:DAT");
        Bytes(0xa9, 15, 0xa2, 8, 0xa0, 15);    // lfn 15, device 8, command channel
        Jsr(SetLfs);
        Jsr(Open);
        Bytes(0xa9, 15);
        Jsr(Close);

        Name("TEST.BIN");
        Bytes(0xa9, 1, 0xa2, 8, 0xa0, 2);      // secondary address 2, headerless to X/Y
        Jsr(SetLfs);
        Bytes(0xa9, 0, 0xa2, LoadAddress & 0xff, 0xa0, LoadAddress >> 8);
        Jsr(Load);
        Bytes(0xdb);                            // stp

        // the names follow the code
        var addresses = new Dictionary<string, int>();
        foreach (var name in fixups.Select(i => i.Name).Distinct())
        {
            addresses[name] = codeStart + code.Count;
            code.AddRange(name.Select(c => (byte)c));
        }

        foreach (var (index, name) in fixups)
        {
            code[index] = (byte)(addresses[name] & 0xff);
            code[index + 2] = (byte)(addresses[name] >> 8);
        }

        byte[] header =
        [
            0x01, 0x08,                          // load address
            0x0b, 0x08, 0x0a, 0x00,              // next line, line 10
            0x9e, (byte)'2', (byte)'0', (byte)'6', (byte)'1', 0x00, // SYS2061
            0x00, 0x00,                          // end of program
        ];

        return [.. header, .. code];
    }
}

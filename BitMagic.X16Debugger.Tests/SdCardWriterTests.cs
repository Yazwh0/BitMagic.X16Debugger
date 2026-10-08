using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.Tests.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;

namespace BitMagic.X16Debugger.Tests;

[TestClass]
public class SdCardWriterTests
{
    private static readonly byte[] Level1 = [0xa9, 0x01, 0x60];

    private Emulator _emulator = null!;
    private SdCard _sdCard = null!;
    private DebugableFileManager _files = null!;
    private string _projectFolder = null!;

    [TestInitialize]
    public void Setup()
    {
        _emulator = new Emulator();
        var idManager = new IdManager();
        _files = new DebugableFileManager(idManager);
        var disassemblerManager = new DisassemblerManager(new SourceMapManager(_emulator, new FakeEmulatorLogger()), _emulator, idManager);
        _files.SetBreakpointManager(new BreakpointManager(_emulator, idManager, disassemblerManager, _files));

        _sdCard = new SdCard(16, new FakeEmulatorLogger());

        _projectFolder = Path.Combine(Path.GetTempPath(), $"sdcardwriter-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(Path.Combine(_projectFolder, "out"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        _emulator.Dispose();
        _sdCard.Dispose();
        System.IO.Directory.Delete(_projectFolder, true);
    }

    // a built file, written to the project's output folder as the build does
    private void AddBuiltFile(string path, byte[] data)
    {
        _files.AddFiles(new Cc65BinaryFile(path, 0x8000, data.Length) { Data = data });

        var hostFile = Path.Combine(_projectFolder, "out", path);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(hostFile)!);
        File.WriteAllBytes(hostFile, data);
        _files.SetHostFile(path, hostFile);
    }

    private void Write(string autobootFile = "", params SdCardFile[] sdCardFiles) =>
        new SdCardWriter(_sdCard, _files, new FakeEmulatorLogger()).Write(sdCardFiles, _projectFolder, [], autobootFile);

    private byte[]? ReadCard(string path)
    {
        if (!_sdCard.FileSystem.FileExists(path))
            return null;

        using var stream = _sdCard.FileSystem.OpenFile(path, FileMode.Open);
        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        return data;
    }

    [TestMethod]
    [DataRow("/DATA/LEVEL1.BIN")]
    [DataRow("/data/level1.bin")]
    public void NotInSdCardFiles_GoesToBuildPath(string sdCardPath)
    {
        AddBuiltFile("DATA/LEVEL1.BIN", Level1);

        Write();

        CollectionAssert.AreEqual(Level1, ReadCard("DATA\\LEVEL1.BIN"));
        Assert.AreEqual("DATA/LEVEL1.BIN", _files.GetFileOnSdCard(sdCardPath)?.Path);
    }

    [TestMethod]
    public void NotInSdCardFiles_NestedFolder_IsCreated()
    {
        AddBuiltFile("GAME/DATA/LEVEL1.BIN", Level1);

        Write();

        CollectionAssert.AreEqual(Level1, ReadCard("GAME\\DATA\\LEVEL1.BIN"));
        Assert.IsNotNull(_files.GetFileOnSdCard("/GAME/DATA/LEVEL1.BIN"));
    }

    // built to the root of the output folder, and moved into a folder by the project
    [TestMethod]
    public void InSdCardFiles_GoesOnlyWhereTheProjectSays()
    {
        AddBuiltFile("LEVEL1.BIN", Level1);

        Write("", new SdCardFile { Source = "out/*.BIN", Dest = "DATA" });

        CollectionAssert.AreEqual(Level1, ReadCard("DATA\\LEVEL1.BIN"));
        Assert.IsNull(ReadCard("LEVEL1.BIN"), "Not also put at the build path.");

        Assert.AreEqual("LEVEL1.BIN", _files.GetFileOnSdCard("/DATA/LEVEL1.BIN")?.Path);
        Assert.IsNull(_files.GetFileOnSdCard("/LEVEL1.BIN"));

        // the build path still finds it, eg for romSource
        Assert.AreEqual("LEVEL1.BIN", _files.GetFile_New("LEVEL1.BIN")?.Path);
    }

    [TestMethod]
    public void InSdCardFilesTwice_IsAtBoth()
    {
        AddBuiltFile("LEVEL1.BIN", Level1);

        Write("",
            new SdCardFile { Source = "out/LEVEL1.BIN", Dest = "DATA" },
            new SdCardFile { Source = "out/LEVEL1.BIN", Dest = "BACKUP" });

        Assert.IsNotNull(_files.GetFileOnSdCard("/DATA/LEVEL1.BIN"));
        Assert.IsNotNull(_files.GetFileOnSdCard("/BACKUP/LEVEL1.BIN"));
    }

    [TestMethod]
    public void InSdCardFiles_NotBuilt_IsCopiedButHasNoDebuggerInfo()
    {
        File.WriteAllBytes(Path.Combine(_projectFolder, "out", "MUSIC.DAT"), [1, 2, 3]);

        Write("", new SdCardFile { Source = "out/MUSIC.DAT", Dest = "DATA" });

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, ReadCard("DATA\\MUSIC.DAT"));
        Assert.IsNull(_files.GetFileOnSdCard("/DATA/MUSIC.DAT"));
    }

    [TestMethod]
    public void Excluded_IsNotPlaced()
    {
        AddBuiltFile("KERNAL.BIN", Level1);

        new SdCardWriter(_sdCard, _files, new FakeEmulatorLogger()).Write([], _projectFolder, ["KERNAL.BIN"], "");

        Assert.IsNull(ReadCard("KERNAL.BIN"));
        Assert.IsNull(_files.GetFileOnSdCard("/KERNAL.BIN"));
    }

    [TestMethod]
    public void Autoboot_LoadsFromWhereTheFileWasPlaced()
    {
        AddBuiltFile("MAIN.PRG", Level1);

        Write("MAIN.PRG", new SdCardFile { Source = "out/MAIN.PRG", Dest = "GAME" });

        var autoboot = Encoding.ASCII.GetString(ReadCard("AUTOBOOT.X16") ?? []);
        StringAssert.Contains(autoboot, "\"GAME/MAIN.PRG\"");
    }

    [TestMethod]
    public void Autoboot_NotInSdCardFiles_LoadsBuildPath()
    {
        AddBuiltFile("MAIN.PRG", Level1);

        Write("MAIN.PRG");

        var autoboot = Encoding.ASCII.GetString(ReadCard("AUTOBOOT.X16") ?? []);
        StringAssert.Contains(autoboot, "\"MAIN.PRG\"");
    }
}

using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.Tests.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BitMagic.X16Debugger.Tests.DebugableFiles;

[TestClass]
public class DebugableFileManagerTests
{
    private Emulator _emulator = null!;
    private DebugableFileManager _debugableFileManager = null!;

    [TestInitialize]
    public void Setup()
    {
        _emulator = new Emulator();
        var idManager = new IdManager();
        _debugableFileManager = new DebugableFileManager(idManager);
        var disassemblerManager = new DisassemblerManager(new SourceMapManager(_emulator, new FakeEmulatorLogger()), _emulator, idManager);
        _debugableFileManager.SetBreakpointManager(new BreakpointManager(_emulator, idManager, disassemblerManager, _debugableFileManager));

        // ld65 names output files with '/'
        _debugableFileManager.AddFiles(new Cc65BinaryFile("DAT/PSM.DAT", 0x8000, 1));
    }

    [TestCleanup]
    public void Cleanup() => _emulator.Dispose();

    [TestMethod]
    [DataRow("DAT/PSM.DAT")]
    [DataRow("DAT\\PSM.DAT")] // SETNAM on Windows
    [DataRow("dat/psm.dat")]
    [DataRow("/DAT/PSM.DAT")]
    public void GetFile_MatchesLoadedFilename(string filename)
    {
        Assert.AreEqual("DAT/PSM.DAT", _debugableFileManager.GetFile_New(filename)?.Path);
    }

    [TestMethod]
    public void GetFile_DifferentFile_NotFound()
    {
        Assert.IsNull(_debugableFileManager.GetFile_New("DAT\\SM.DAT"));
    }

    // romSource entries can name a file by its filename alone, eg 'kernal.bin' for ld65's 'build/x16/kernal.bin'.
    [TestMethod]
    [DataRow("PSM.DAT")]
    [DataRow("psm.dat")]
    [DataRow("app/PSM.DAT")]
    [DataRow("app\\psm.dat")]
    public void GetFileByName_MatchesFilenameInAnyFolder(string filename)
    {
        Assert.AreEqual("DAT/PSM.DAT", _debugableFileManager.GetFileByName(filename)?.Path);
    }

    [TestMethod]
    public void GetFileByName_Ambiguous_NotFound()
    {
        _debugableFileManager.AddFiles(new Cc65BinaryFile("OTHER/PSM.DAT", 0x8000, 1));

        Assert.IsNull(_debugableFileManager.GetFileByName("PSM.DAT"));
    }
}

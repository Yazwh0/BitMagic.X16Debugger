using BitMagic.X16Debugger.Builder;
using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.Tests.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BitMagic.X16Debugger.Tests.DebugableFiles;

// The 'outputFilename' on a bitmagic project file names the main output, which is then used for writing, the SD
// card, autoboot and matching LOADs.
[TestClass]
public class BitmagicOutputFilenameTests
{
    private string _folder = "";

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "BitMagicOutputTest" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "main.bmasm"), ".machine CommanderX16R40\n    lda #1\n    stp\n");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_folder, true);

    private async Task<BitMagicBinaryFile> Build(string? outputFilename)
    {
        var serviceManager = new ServiceManager(_ => new Emulator(), new FakeEmulatorLogger());
        var (wrapper, _) = await serviceManager.BitmagicBuilder.Build("main.bmasm", _folder, null, outputFilename);

        Assert.IsNotNull(wrapper);
        return (BitMagicBinaryFile)wrapper.Source;
    }

    [TestMethod]
    public async Task NotSet_SourceNameWithPrg()
    {
        var file = await Build(null);

        Assert.AreEqual("MAIN.PRG", file.Path);
        Assert.AreEqual(FileHeader.HeaderNotInCode, file.HasHeader);
    }

    [TestMethod]
    public async Task Set_UsedForMainFile()
    {
        var file = await Build("game.prg");

        Assert.AreEqual("GAME.PRG", file.Path);
        Assert.AreEqual(FileHeader.HeaderNotInCode, file.HasHeader);
    }

    [TestMethod]
    [DataRow("BIN/GAME.PRG")]
    [DataRow("BIN\\GAME.PRG")]
    public async Task InFolder_AutobootUsesFullPath(string outputFilename)
    {
        var file = await Build(outputFilename);

        Assert.AreEqual("BIN/GAME.PRG", file.Path);
        Assert.AreEqual("BIN/GAME.PRG", ProjectBuilder.AutobootPath(file));
    }

    // The main output is a program, so it has a header whatever it's called, eg a command named 'CAT'.
    [TestMethod]
    public async Task NoExtension_HasHeader()
    {
        var file = await Build("CAT");

        Assert.AreEqual("CAT", file.Path);
        Assert.AreEqual(FileHeader.HeaderNotInCode, file.HasHeader);
    }

    [TestMethod]
    public async Task Bin_NoHeader()
    {
        var file = await Build("GAME.BIN");

        Assert.AreEqual("GAME.BIN", file.Path);
        Assert.AreEqual(FileHeader.NoHeader, file.HasHeader);
    }

    [TestMethod]
    public async Task X16_HasHeader()
    {
        var file = await Build("GAME.X16");

        Assert.AreEqual(FileHeader.HeaderNotInCode, file.HasHeader);
    }
}

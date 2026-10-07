using BitMagic.X16Debugger.Tests.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BitMagic.X16Debugger.Tests;

[TestClass]
public class X16DirectoryTrackerTests
{
    private const int CurVolume = X16DirectoryTracker.DefaultCurVolumeAddress - 0xa000;

    private Emulator _emulator = null!;
    private SdCard _sdCard = null!;
    private X16DirectoryTracker _tracker = null!;

    [TestInitialize]
    public void Setup()
    {
        _emulator = new Emulator();
        _sdCard = new SdCard(16, new FakeEmulatorLogger());
        _emulator.LoadSdCard(_sdCard);
        _tracker = new X16DirectoryTracker(_emulator, new FakeEmulatorLogger());

        _sdCard.FileSystem.CreateDirectory("GAME");
        _sdCard.FileSystem.CreateDirectory("GAME\\DATA");
    }

    [TestCleanup]
    public void Cleanup()
    {
        _emulator.Dispose();
        _sdCard.Dispose();
    }

    // Sets up fat32's cur_volume as the DOS would.
    private void SetDosState(bool mounted, uint rootCluster, uint cwdCluster)
    {
        var ram = _emulator.RamBank;
        ram[CurVolume] = (byte)(mounted ? 1 : 0);
        BitConverter.GetBytes(rootCluster).CopyTo(ram[(CurVolume + 1)..]);
        BitConverter.GetBytes(cwdCluster).CopyTo(ram[(CurVolume + 39)..]);
    }

    private uint RootCluster => (uint)_sdCard.FileSystem.RootDirectoryCluster;

    private uint ClusterOf(string path)
    {
        var directory = _sdCard.FileSystem.RootDir;
        BitMagic.DiscUtils.Fat.DirectoryEntry? entry = null;

        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            entry = directory.GetDirectories().First(i => i.Name.ToString() == part);
            directory = directory.GetChildDirectory(entry.Name);
        }

        return entry!.FirstCluster;
    }

    [TestMethod]
    public void NotMounted_IsRoot()
    {
        SetDosState(false, RootCluster, ClusterOf("GAME"));

        Assert.AreEqual("/", _tracker.Refresh());
    }

    [TestMethod]
    public void AtRoot()
    {
        SetDosState(true, RootCluster, RootCluster);

        Assert.AreEqual("/", _tracker.Refresh());
    }

    [TestMethod]
    [DataRow("/GAME")]
    [DataRow("/GAME/DATA")]
    public void InDirectory(string path)
    {
        SetDosState(true, RootCluster, ClusterOf(path));

        Assert.AreEqual(path, _tracker.Refresh());
        Assert.AreEqual(path, _tracker.CurrentDirectory);
    }

    [TestMethod]
    public void Resolve_IsRelativeToCurrentDirectory()
    {
        SetDosState(true, RootCluster, ClusterOf("GAME"));

        Assert.AreEqual("/GAME/LEVEL1.PRG", _tracker.Resolve("LEVEL1.PRG"));
        Assert.AreEqual("/LEVEL1.PRG", _tracker.Resolve("//:LEVEL1.PRG"));
    }

    [TestMethod]
    public void DirectoryCreatedLater_IsFound()
    {
        SetDosState(true, RootCluster, ClusterOf("GAME"));
        Assert.AreEqual("/GAME", _tracker.Refresh());

        _sdCard.FileSystem.CreateDirectory("GAME\\SAVES");
        SetDosState(true, RootCluster, ClusterOf("GAME/SAVES"));

        Assert.AreEqual("/GAME/SAVES", _tracker.Refresh());
    }

    [TestMethod]
    public void UnknownCluster_IsRoot()
    {
        SetDosState(true, RootCluster, 0x1234);

        Assert.AreEqual("/", _tracker.Refresh());
    }

    // eg a ROM where cur_volume isn't at the default address
    [TestMethod]
    public void WrongRootCluster_IsRoot()
    {
        SetDosState(true, RootCluster + 1, ClusterOf("GAME"));

        Assert.AreEqual("/", _tracker.Refresh());
    }

    [TestMethod]
    public void FindCurVolume_UsesSymbols()
    {
        var symbols = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(symbols, ["al 00B7FF .volume_idx", "al 00B900 .cur_volume"]);
            _tracker.FindCurVolume([new SymbolsFile { Symbols = symbols, RomBank = 3 }]);

            // the default address no longer matters
            SetDosState(true, RootCluster + 1, 0);

            var ram = _emulator.RamBank;
            var curVolume = 0xb900 - 0xa000;
            ram[curVolume] = 1;
            BitConverter.GetBytes(RootCluster).CopyTo(ram[(curVolume + 1)..]);
            BitConverter.GetBytes(ClusterOf("GAME")).CopyTo(ram[(curVolume + 39)..]);

            Assert.AreEqual("/GAME", _tracker.Refresh());
        }
        finally
        {
            File.Delete(symbols);
        }
    }
}

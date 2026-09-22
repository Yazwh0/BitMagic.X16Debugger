using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.Scopes;
using BitMagic.X16Debugger.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace BitMagic.X16Debugger.Tests.Variables;

// Exercises the VERA scope's editable VariableMaps, the way X16Debug.HandleSetVariableRequest does:
// look the entry up by name (under "VERA" directly, or nested under a VariableChildren group like
// "Layer 0"), then SetVariable()/GetVariable() it. Setters here write Emulator.Vera.* (the decoded
// fields), not the $9F20-$9F3F memory bytes - that's deliberate and safe because vera_init
// (Vera.asm) re-encodes those fields back into memory on every emulator run/step, see the
// SetRegister comment in VariablesManager.cs. These tests only check the decoded side; they don't
// exercise vera_init itself (that needs a running emulator, covered by BitMagic.X16Emulator.Tests).
[TestClass]
public class VariableManagerVeraScopeTests
{
    private Emulator _emulator = null!;
    private ScopeManager _scopeManager = null!;
    private VariableManager _variableManager = null!;

    [TestInitialize]
    public void Setup()
    {
        _emulator = new Emulator();

        var idManager = new IdManager();
        var logger = new FakeEmulatorLogger();
        var debugableFileManager = new DebugableFileManager(idManager);
        var sourceMapManager = new SourceMapManager(_emulator, logger);
        _scopeManager = new ScopeManager(idManager);
        var disassemblerManager = new DisassemblerManager(sourceMapManager, _emulator, idManager);
        var stackManager = new StackManager(_emulator, idManager, sourceMapManager, disassemblerManager, debugableFileManager);
        var spriteManager = new SpriteManager(_emulator);
        var paletteManager = new PaletteManager(_emulator);
        var psgManager = new PsgManager(_emulator);

        _variableManager = new VariableManager(idManager, _emulator, _scopeManager, paletteManager, spriteManager, stackManager, psgManager);
    }

    [TestCleanup]
    public void Cleanup() => _emulator.Dispose();

    private VariableMap GetVeraVariable(string name) =>
        (VariableMap)_scopeManager.GetScope("VERA", false).Variables.First(v => v.Name == name);

    private VariableMap GetVeraChild(string groupName, string childName) =>
        (VariableMap)((VariableChildren)_scopeManager.GetScope("VERA", false).Variables.First(v => v.Name == groupName)).Children.First(v => v.Name == childName);

    private static void Set(IVariableItem variable, string name, string value) =>
        variable.SetVariable(new SetVariableArguments { Name = name, Value = value });

    [TestMethod]
    public void Data0Address_SetVariable_WritesDecodedField()
    {
        var address = GetVeraChild("Data 0", "Address");

        Set(address, "Address", "0x1F800");

        Assert.AreEqual(0x1F800, _emulator.Vera.Data0_Address);
    }

    [TestMethod]
    public void Data0Step_SetVariable_WritesDecodedField()
    {
        var step = GetVeraChild("Data 0", "Step");

        Set(step, "Step", "4");

        Assert.AreEqual(4, _emulator.Vera.Data0_Step);
    }

    [TestMethod]
    public void Data1Address_SetVariable_WritesDecodedField()
    {
        var address = GetVeraChild("Data 1", "Address");

        Set(address, "Address", "$0100");

        Assert.AreEqual(0x0100, _emulator.Vera.Data1_Address);
    }

    [TestMethod]
    public void Data0Address_SetVariable_UnparsableText_LeavesFieldUnchanged()
    {
        var address = GetVeraChild("Data 0", "Address");
        _emulator.Vera.Data0_Address = 0x1234;

        Set(address, "Address", "not-a-number");

        Assert.AreEqual(0x1234, _emulator.Vera.Data0_Address);
    }

    [TestMethod]
    public void Layer0MapAddress_SetVariable_WritesDecodedField()
    {
        var mapAddress = GetVeraChild("Layer 0", "Map Address");

        Set(mapAddress, "Map Address", "0x10000");

        Assert.AreEqual((uint)0x10000, _emulator.Vera.Layer0_MapAddress);
    }

    [TestMethod]
    public void Layer0TileAddress_SetVariable_WritesDecodedField()
    {
        var tileAddress = GetVeraChild("Layer 0", "Tile Address");

        Set(tileAddress, "Tile Address", "0x04000");

        Assert.AreEqual((uint)0x04000, _emulator.Vera.Layer0_TileAddress);
    }

    [TestMethod]
    public void Layer0HScroll_SetVariable_WritesDecodedField()
    {
        var hScroll = GetVeraChild("Layer 0", "HScroll");

        Set(hScroll, "HScroll", "300");

        Assert.AreEqual((ushort)300, _emulator.Vera.Layer0_HScroll);
    }

    [TestMethod]
    public void Layer0VScroll_SetVariable_WritesDecodedField()
    {
        var vScroll = GetVeraChild("Layer 0", "VScroll");

        Set(vScroll, "VScroll", "150");

        Assert.AreEqual((ushort)150, _emulator.Vera.Layer0_VScroll);
    }

    [TestMethod]
    public void Layer0TileWidth_SetVariable_16_SetsWideBit()
    {
        var tileWidth = GetVeraChild("Layer 0", "Tile Width");
        _emulator.Vera.Layer0_TileWidth = 0;

        Set(tileWidth, "Tile Width", "16");

        Assert.AreEqual((byte)1, _emulator.Vera.Layer0_TileWidth);
    }

    [TestMethod]
    public void Layer0TileWidth_SetVariable_8_ClearsWideBit()
    {
        var tileWidth = GetVeraChild("Layer 0", "Tile Width");
        _emulator.Vera.Layer0_TileWidth = 1;

        Set(tileWidth, "Tile Width", "8");

        Assert.AreEqual((byte)0, _emulator.Vera.Layer0_TileWidth);
    }

    [TestMethod]
    public void Layer0TileWidth_SetVariable_InvalidSize_LeavesFieldUnchanged()
    {
        var tileWidth = GetVeraChild("Layer 0", "Tile Width");
        _emulator.Vera.Layer0_TileWidth = 0;

        Set(tileWidth, "Tile Width", "12");

        Assert.AreEqual((byte)0, _emulator.Vera.Layer0_TileWidth);
    }

    [TestMethod]
    public void Layer0TileHeight_SetVariable_16_SetsTallBit()
    {
        var tileHeight = GetVeraChild("Layer 0", "Tile Height");
        _emulator.Vera.Layer0_TileHeight = 0;

        Set(tileHeight, "Tile Height", "16");

        Assert.AreEqual((byte)1, _emulator.Vera.Layer0_TileHeight);
    }

    [TestMethod]
    public void Layer0MapWidth_SetVariable_128_SetsIndex2()
    {
        var mapWidth = GetVeraChild("Layer 0", "Map Width");

        Set(mapWidth, "Map Width", "128");

        Assert.AreEqual((byte)2, _emulator.Vera.Layer0_MapWidth);
    }

    [TestMethod]
    public void Layer0MapHeight_SetVariable_256_SetsIndex3()
    {
        var mapHeight = GetVeraChild("Layer 0", "Map Height");

        Set(mapHeight, "Map Height", "256");

        Assert.AreEqual((byte)3, _emulator.Vera.Layer0_MapHeight);
    }

    [TestMethod]
    public void Layer0MapWidth_SetVariable_InvalidSize_LeavesFieldUnchanged()
    {
        var mapWidth = GetVeraChild("Layer 0", "Map Width");
        _emulator.Vera.Layer0_MapWidth = 1;

        Set(mapWidth, "Map Width", "100");

        Assert.AreEqual((byte)1, _emulator.Vera.Layer0_MapWidth);
    }

    [TestMethod]
    public void Layer1MapAddress_SetVariable_WritesDecodedField()
    {
        var mapAddress = GetVeraChild("Layer 1", "Map Address");

        Set(mapAddress, "Map Address", "0x08000");

        Assert.AreEqual((uint)0x08000, _emulator.Vera.Layer1_MapAddress);
    }

    [TestMethod]
    public void Layer1TileWidth_SetVariable_16_SetsWideBit()
    {
        var tileWidth = GetVeraChild("Layer 1", "Tile Width");
        _emulator.Vera.Layer1_TileWidth = 0;

        Set(tileWidth, "Tile Width", "16");

        Assert.AreEqual((byte)1, _emulator.Vera.Layer1_TileWidth);
    }

    [TestMethod]
    public void DcSel_SetVariable_WritesDecodedField()
    {
        var dcSel = GetVeraVariable("DcSel");

        Set(dcSel, "DcSel", "1");

        Assert.AreEqual((byte)1, _emulator.Vera.DcSel);
    }

    [TestMethod]
    public void Output_SetVariable_WritesDecodedField()
    {
        var output = GetVeraVariable("Output");

        Set(output, "Output", "2");

        Assert.AreEqual((uint)2, _emulator.Vera.VideoOutput);
    }

    [TestMethod]
    public void DcHStart_SetVariable_WritesDecodedField()
    {
        var hStart = GetVeraVariable("DC HStart");

        Set(hStart, "DC HStart", "0x10");

        Assert.AreEqual((ushort)0x10, _emulator.Vera.Dc_HStart);
    }

    [TestMethod]
    public void DcVStart_SetVariable_WritesDecodedField()
    {
        var vStart = GetVeraVariable("DC VStart");

        Set(vStart, "DC VStart", "0x20");

        Assert.AreEqual((ushort)0x20, _emulator.Vera.Dc_VStart);
    }

    [TestMethod]
    public void DcHStop_SetVariable_WritesDecodedField()
    {
        var hStop = GetVeraVariable("DC HStop");

        Set(hStop, "DC HStop", "0x320");

        Assert.AreEqual((ushort)0x320, _emulator.Vera.Dc_HStop);
    }

    [TestMethod]
    public void DcVStop_SetVariable_WritesDecodedField()
    {
        var vStop = GetVeraVariable("DC VStop");

        Set(vStop, "DC VStop", "0x1E0");

        Assert.AreEqual((ushort)0x1E0, _emulator.Vera.Dc_VStop);
    }
}

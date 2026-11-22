using BitMagic.Common.Address;
using BitMagic.Decompiler;
using BitMagic.X16Debugger.Tests.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BitMagic.X16Debugger.Tests;

// ROM banks are disassembled the first time they're used, not at launch. Named banks have an entry from the start (so
// they're in Loaded Sources and restored breakpoints find them), unnamed ones are created on request.
[TestClass]
public class RomDisassemblyTests
{
    private ServiceManager _serviceManager = null!;
    private Emulator Emulator => _serviceManager.Emulator;
    private DisassemblerManager Disassembler => _serviceManager.DisassemblerManager;

    [TestInitialize]
    public void Setup()
    {
        _serviceManager = new ServiceManager(_ => new Emulator(), new FakeEmulatorLogger());
        Disassembler.SetProject(new X16DebugProject());   // the 16 standard bank names
    }

    // lda #$01, rts at $C000 in the bank
    private void PutCode(int bank)
    {
        Emulator.RomBank[bank * 0x4000 + 0] = 0xa9;
        Emulator.RomBank[bank * 0x4000 + 1] = 0x01;
        Emulator.RomBank[bank * 0x4000 + 2] = 0x60;
    }

    [TestMethod]
    public void NamedBanks_RegisteredButNotDisassembled()
    {
        var kernal = Disassembler.GetByPath("Rom/Kernal.bmasm");

        Assert.IsNotNull(kernal);
        Assert.AreEqual(0, kernal.RomBank);
        Assert.AreEqual(0, kernal.Items.Count);
        for (var i = 0; i < 16; i++)
            Assert.IsFalse(Disassembler.IsRomBankDisassembled(i), $"bank {i}");
        Assert.IsFalse(Disassembler.DecompiledData.ContainsKey("Rom/RomBank_16.bmasm"), "unnamed banks aren't created until they're used");
    }

    [TestMethod]
    public void GetRomBank_DisassemblesOnce()
    {
        PutCode(0);

        var first = Disassembler.GetRomBank(0);
        var items = first.Items;

        StringAssert.Contains(first.Items[1].Instruction, "lda #$01");
        Assert.IsTrue(Disassembler.IsRomBankDisassembled(0));

        Emulator.RomBank[0] = 0xea;     // changing the bank doesn't redo it
        var second = Disassembler.GetRomBank(0);

        Assert.AreSame(first, second);
        Assert.AreSame(items, second.Items);
    }

    [TestMethod]
    public void UnnamedBank_CreatedOnRequest_AndAnnounced()
    {
        var created = new List<DecompileReturn>();
        Disassembler.RomBankCreated = created.Add;
        PutCode(40);

        var id = Disassembler.GetDisassemblyId(0xc000, 0, 40);
        var bank = _serviceManager.IdManager.GetObject<DecompileReturn>(id);

        Assert.IsNotNull(bank);
        Assert.AreEqual("Rom/RomBank_40.bmasm", bank.Path);
        Assert.AreEqual(1, created.Count);
        Assert.IsFalse(Disassembler.IsRomBankDisassembled(40), "only when its content is used");

        bank.GetContent();      // as a stack frame does

        Assert.IsTrue(Disassembler.IsRomBankDisassembled(40));
        StringAssert.Contains(bank.Items[1].Instruction, "lda #$01");

        Disassembler.GetDisassemblyId(0xc000, 0, 40);
        Assert.AreEqual(1, created.Count, "announced once");
    }

    [TestMethod]
    public void GetByPath()
    {
        Assert.AreEqual(40, Disassembler.GetByPath("Rom/RomBank_40.bmasm")?.RomBank);
        Assert.IsNull(Disassembler.GetByPath("Rom/RomBank_x.bmasm"));
        Assert.IsNull(Disassembler.GetByPath("Rom/RomBank_300.bmasm"));
        Assert.IsNull(Disassembler.GetByPath("Rom/Nope.bmasm"));
    }

    // as VS Code does at launch for a breakpoint it kept from an earlier session
    [TestMethod]
    [DataRow("Rom/Kernal.bmasm", 0)]
    [DataRow("Rom/RomBank_40.bmasm", 40)]
    public void Breakpoint_ByPath_BeforeTheBankIsUsed(string path, int bankNumber)
    {
        PutCode(bankNumber);

        var response = _serviceManager.BreakpointManager.HandleSetBreakpointsRequest(new SetBreakpointsArguments
        {
            Source = new Source { Path = path, Name = Path.GetFileName(path) },
            Breakpoints = [new SourceBreakpoint { Line = 1 }]
        });

        var breakpoint = response.Breakpoints.Single();
        Assert.IsTrue(breakpoint.Verified);
        Assert.AreEqual(1, breakpoint.Line);
        Assert.IsTrue(Disassembler.IsRomBankDisassembled(bankNumber));

        var (_, rom) = AddressFunctions.GetMemoryLocations(bankNumber, 0xc000);
        Assert.AreNotEqual(0, Emulator.Breakpoints[rom], "set on $C000 in the bank");
    }

    [TestMethod]
    public void DisassembleRequest_UnusedBank()
    {
        PutCode(40);

        var response = Disassembler.HandleDisassembleRequest(new DisassembleArguments
        {
            MemoryReference = AddressFunctions.GetDebuggerAddressString(0xc000, 0, 40),
            InstructionCount = 2
        });

        Assert.AreEqual(2, response.Instructions.Count);
        StringAssert.Contains(response.Instructions[0].Instruction, "lda #$01");
        Assert.IsTrue(Disassembler.IsRomBankDisassembled(40));
    }
}

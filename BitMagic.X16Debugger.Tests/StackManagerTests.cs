using BitMagic.Common.Address;
using BitMagic.X16Debugger.Tests.Variables;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BitMagic.X16Debugger.Tests;

// The top of the call stack should be the instruction that stopped the CPU. After an STP the Pc has moved past it, and
// after a BRK (when BRK stops the emulator) the Pc is already at the IRQ handler.
[TestClass]
public class StackManagerTests
{
    private const int Start = 0x810;

    private static ServiceManager Run(byte[] code, Emulator.EmulatorResult expected, bool brkCausesStop = false)
    {
        var serviceManager = new ServiceManager(_ => new Emulator(), new FakeEmulatorLogger());
        var emulator = serviceManager.Emulator;

        code.CopyTo(emulator.Memory[Start..]);
        emulator.Pc = Start;
        emulator.Brk_Causes_Stop = brkCausesStop;

        var result = emulator.Emulate();
        Assert.AreEqual(expected, result);

        serviceManager.StackManager.StopReason = result;
        serviceManager.StackManager.GenerateCallStack();

        return serviceManager;
    }

    private static string Reference(int address) => AddressFunctions.GetDebuggerAddressString(address, 0, 0);

    [TestMethod]
    public void Stp_ShowsTheStp()
    {
        var serviceManager = Run([0xea, 0xdb], Emulator.EmulatorResult.DebugOpCode);   // nop, stp
        Assert.AreEqual(Start + 2, serviceManager.Emulator.Pc, "The Pc should have moved past the STP.");

        var top = serviceManager.StackManager.CallStack.First().StackFrame;

        Assert.AreEqual(Reference(Start + 1), top.InstructionPointerReference);
        StringAssert.StartsWith(top.Name, "> STP: ");
    }

    [TestMethod]
    public void Brk_ShowsTheBrk()
    {
        var serviceManager = Run([0xea, 0x00, 0x00], Emulator.EmulatorResult.BrkHit, brkCausesStop: true);   // nop, brk + signature
        Assert.AreNotEqual(Start + 1, serviceManager.Emulator.Pc, "The Pc should be at the IRQ handler.");

        var frames = serviceManager.StackManager.CallStack.Select(i => i.StackFrame).ToList();

        Assert.AreEqual(Reference(Start + 1), frames[0].InstructionPointerReference);
        StringAssert.StartsWith(frames[0].Name, "> BRK: ");
        Assert.AreEqual(1, frames.Count, "The BRK's own frame from the stack shouldn't be repeated.");
    }

    // A JSR in banked RAM should be on the call stack. The CPU copies it into bank 2 and runs it. The JSR goes to $0900,
    // which either stops straight away, so bank 2 is still current (the core copies it back to RamBank when it stops),
    // or switches to bank 3 first.
    [TestMethod]
    [DataRow(false, DisplayName = "JsrInBankedRam_CurrentBank")]
    [DataRow(true, DisplayName = "JsrInBankedRam_OtherBank")]
    public void JsrInBankedRam(bool switchBank)
    {
        var serviceManager = new ServiceManager(_ => new Emulator(), new FakeEmulatorLogger());
        var emulator = serviceManager.Emulator;

        byte[] main = [
            0xa9, 0x02,             // lda #2
            0x85, 0x00,             // sta $00      ; ram bank 2
            0xa2, 0x02,             // ldx #2
            0xbd, 0x80, 0x08,       // lda $0880, x
            0x9d, 0x00, 0xa0,       // sta $a000, x
            0xca,                   // dex
            0x10, 0xf7,             // bpl -9
            0x4c, 0x00, 0xa0,       // jmp $a000
        ];
        main.CopyTo(emulator.Memory[Start..]);

        byte[] banked = [0x20, 0x00, 0x09];     // jsr $0900, copied to $a000
        banked.CopyTo(emulator.Memory[0x880..]);

        byte[] sub = switchBank
            ? [0xa9, 0x03, 0x85, 0x00, 0xdb]    // lda #3, sta $00, stp
            : [0xdb];                           // stp
        sub.CopyTo(emulator.Memory[0x900..]);

        emulator.Pc = Start;
        var result = emulator.Emulate();
        Assert.AreEqual(Emulator.EmulatorResult.DebugOpCode, result);
        Assert.AreEqual(switchBank ? 3 : 2, emulator.Memory[0x00], "RAM bank");

        serviceManager.StackManager.StopReason = result;
        serviceManager.StackManager.GenerateCallStack();

        var frames = serviceManager.StackManager.CallStack.Select(i => i.StackFrame).ToList();

        Assert.AreEqual(2, frames.Count, string.Join(", ", frames.Select(i => i.Name)));
        Assert.AreEqual(AddressFunctions.GetDebuggerAddressString(0xa000, 2, 0), frames[1].InstructionPointerReference);
        Assert.IsFalse(frames[1].Name.StartsWith("BRK"), frames[1].Name);
    }

    // the same stop as Stp_ShowsTheStp, but without knowing why it stopped, so the Pc is shown as it is
    [TestMethod]
    public void NoStopReason_ShowsThePc()
    {
        var serviceManager = Run([0xea, 0xdb], Emulator.EmulatorResult.DebugOpCode);
        serviceManager.StackManager.StopReason = null;
        serviceManager.StackManager.GenerateCallStack();

        var top = serviceManager.StackManager.CallStack.First().StackFrame;

        Assert.AreEqual(Reference(Start + 2), top.InstructionPointerReference);
        Assert.AreEqual("> ", top.Name[..2]);
        Assert.IsFalse(top.Name.StartsWith("> STP: "));
    }
}

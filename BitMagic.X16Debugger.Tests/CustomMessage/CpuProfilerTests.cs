using BitMagic.X16Debugger.CustomMessage;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BitMagic.X16Debugger.Tests.CustomMessage;

// The CPU profiler reads the emulator's history, a ring buffer. A new emulator's write position is 0, so its latest
// entry is the last one in the buffer.
[TestClass]
public class CpuProfilerTests
{
    // nothing recorded, so there's no frame to find: a blank image, rather than an index of -1 or searching forever
    [TestMethod]
    public void NoHistory()
    {
        using var emulator = new Emulator();
        Assert.AreEqual(0ul, emulator.HistoryPosition);

        var response = CpuProfilerRequestHandler.GetCpuHistory(emulator);

        Assert.IsFalse(string.IsNullOrEmpty(response.Display));
    }

    // two frame boundaries just before the write position, so the frame between them is drawn
    [TestMethod]
    public void FrameBeforeTheWritePosition()
    {
        using var emulator = new Emulator();
        var history = emulator.History;

        // forwards: the end of one frame, a whole frame, then the start of the current one, finishing at the last entry
        int[] lines = [.. Enumerable.Range(520, 5), .. Enumerable.Range(0, 525), .. Enumerable.Range(0, 3)];
        var start = history.Length - lines.Length;
        for (var i = 0; i < lines.Length; i++)
        {
            history[start + i].CpuY = (ushort)lines[i];
            history[start + i].Clock = 1000ul + (ulong)i * 256;  // a line's worth of cycles each
            history[start + i].PC = 0x810;
        }

        var response = CpuProfilerRequestHandler.GetCpuHistory(emulator);

        Assert.IsFalse(string.IsNullOrEmpty(response.Display));
    }
}

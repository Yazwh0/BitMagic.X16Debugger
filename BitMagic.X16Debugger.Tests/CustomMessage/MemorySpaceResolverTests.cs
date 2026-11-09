using BitMagic.X16Debugger.CustomMessage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BitMagic.X16Debugger.Tests.CustomMessage;

// writeMemory from the memory view, which used to throw when it went past the end, eg of the SD card
[TestClass]
public class MemorySpaceResolverTests
{
    [TestMethod]
    [DataRow(0, 4, new byte[] { 1, 2, 3, 4, 0, 0, 0, 0 })]
    [DataRow(6, 2, new byte[] { 0, 0, 0, 0, 0, 0, 1, 2 })]     // only what fits
    [DataRow(8, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 })]     // starts at the end
    [DataRow(100, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 })]   // past the end
    [DataRow(-1, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 })]
    public void Write(int offset, int written, byte[] expected)
    {
        var memory = new byte[8];

        Assert.AreEqual(written, MemorySpaceResolver.Write(memory, offset, new byte[] { 1, 2, 3, 4 }));
        CollectionAssert.AreEqual(expected, memory);
    }
}

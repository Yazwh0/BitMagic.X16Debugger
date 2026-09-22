using BitMagic.Common;
using BitMagic.X16Debugger.Scopes;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BitMagic.X16Debugger.Tests.Scopes;

[TestClass]
public class IAsmVariableExtensionsTests
{
    private Emulator _emulator = null!;

    [TestInitialize]
    public void Setup() => _emulator = new Emulator();

    [TestCleanup]
    public void Cleanup() => _emulator.Dispose();

    [DataTestMethod]
    [DataRow(VariableDataType.Byte, true)]
    [DataRow(VariableDataType.Sbyte, true)]
    [DataRow(VariableDataType.Short, true)]
    [DataRow(VariableDataType.Ushort, true)]
    [DataRow(VariableDataType.Int, true)]
    [DataRow(VariableDataType.Uint, true)]
    [DataRow(VariableDataType.Long, true)]
    [DataRow(VariableDataType.Ulong, true)]
    [DataRow(VariableDataType.Ptr, true)]
    [DataRow(VariableDataType.Char, false)]
    [DataRow(VariableDataType.String, false)]
    [DataRow(VariableDataType.FixedStrings, false)]
    [DataRow(VariableDataType.BytePtr, false)]
    [DataRow(VariableDataType.UshortPtr, false)]
    [DataRow(VariableDataType.Constant, false)]
    [DataRow(VariableDataType.ProcStart, false)]
    public void SupportsDirectWrite_OnlyFixedWidthNumericTypes(VariableDataType dataType, bool expected)
    {
        var variable = new FakeAsmVariable { VariableDataType = dataType };

        Assert.AreEqual(expected, variable.SupportsDirectWrite());
    }

    [TestMethod]
    public void TrySetValue_WritesByte_FromDecimalText()
    {
        var variable = new FakeAsmVariable { Value = 0x100, VariableDataType = VariableDataType.Byte };

        var result = variable.TrySetValue(_emulator, "200");

        Assert.IsTrue(result);
        Assert.AreEqual((byte)200, _emulator.Memory[0x100]);
    }

    [TestMethod]
    public void TrySetValue_WritesUshort_From0xHexText()
    {
        var variable = new FakeAsmVariable { Value = 0x100, VariableDataType = VariableDataType.Ushort };

        var result = variable.TrySetValue(_emulator, "0x1234");

        Assert.IsTrue(result);
        Assert.AreEqual((ushort)0x1234, BitConverter.ToUInt16(_emulator.Memory.Slice(0x100, 2)));
    }

    [TestMethod]
    public void TrySetValue_WritesInt_FromDollarHexText()
    {
        var variable = new FakeAsmVariable { Value = 0x100, VariableDataType = VariableDataType.Int };

        var result = variable.TrySetValue(_emulator, "$000004D2");

        Assert.IsTrue(result);
        Assert.AreEqual(0x4D2, BitConverter.ToInt32(_emulator.Memory.Slice(0x100, 4)));
    }

    [TestMethod]
    public void TrySetValue_WritesUlong()
    {
        var variable = new FakeAsmVariable { Value = 0x100, VariableDataType = VariableDataType.Ulong };

        var result = variable.TrySetValue(_emulator, "18446744073709551615");

        Assert.IsTrue(result);
        Assert.AreEqual(ulong.MaxValue, BitConverter.ToUInt64(_emulator.Memory.Slice(0x100, 8)));
    }

    [TestMethod]
    public void TrySetValue_HonoursArrayIndexOffset()
    {
        var variable = new FakeAsmVariable { Value = 0x100, VariableDataType = VariableDataType.Byte, Array = true, Length = 4 };

        variable.TrySetValue(_emulator, "9", index: 3);

        Assert.AreEqual((byte)9, _emulator.Memory[0x103]);
        Assert.AreEqual((byte)0, _emulator.Memory[0x100]);
    }

    [TestMethod]
    public void TrySetValue_UnsupportedType_ReturnsFalseAndLeavesMemoryUntouched()
    {
        var variable = new FakeAsmVariable { Value = 0x100, VariableDataType = VariableDataType.String };

        var result = variable.TrySetValue(_emulator, "hello");

        Assert.IsFalse(result);
        Assert.AreEqual((byte)0, _emulator.Memory[0x100]);
    }

    [TestMethod]
    public void TrySetValue_UnparsableText_ReturnsFalse()
    {
        var variable = new FakeAsmVariable { Value = 0x100, VariableDataType = VariableDataType.Byte };

        var result = variable.TrySetValue(_emulator, "not-a-number");

        Assert.IsFalse(result);
    }
}

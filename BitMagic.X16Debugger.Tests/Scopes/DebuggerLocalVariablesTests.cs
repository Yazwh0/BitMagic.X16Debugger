using BitMagic.Common;
using BitMagic.X16Debugger.Scopes;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BitMagic.X16Debugger.Tests.Scopes;

[TestClass]
public class DebuggerLocalVariablesTests
{
    private Emulator _emulator = null!;

    [TestInitialize]
    public void Setup() => _emulator = new Emulator();

    [TestCleanup]
    public void Cleanup() => _emulator.Dispose();

    [TestMethod]
    public void GetArraySetter_UnsupportedElementType_ReturnsNull()
    {
        var variable = new FakeAsmVariable { VariableDataType = VariableDataType.String, Array = true, Length = 4 };

        var setter = DebuggerLocalVariables.GetArraySetter(variable, _emulator);

        Assert.IsNull(setter);
    }

    [TestMethod]
    public void GetArraySetter_WritesTheElementAtTheGivenIndex()
    {
        var variable = new FakeAsmVariable { Value = 0x200, VariableDataType = VariableDataType.Byte, Array = true, Length = 4 };
        var setter = DebuggerLocalVariables.GetArraySetter(variable, _emulator)!;

        setter("2", "77");

        Assert.AreEqual((byte)77, _emulator.Memory[0x202]);
        Assert.AreEqual((byte)0, _emulator.Memory[0x200]);
    }

    [TestMethod]
    public void GetArraySetter_OutOfRangeOrNonNumericIndex_IsIgnoredWithoutThrowing()
    {
        var variable = new FakeAsmVariable { Value = 0x200, VariableDataType = VariableDataType.Byte, Array = true, Length = 4 };
        var setter = DebuggerLocalVariables.GetArraySetter(variable, _emulator)!;

        setter("99", "1");
        setter("-1", "1");
        setter("not-a-number", "1");

        CollectionAssert.AreEqual(new byte[4], _emulator.Memory.Slice(0x200, 4).ToArray());
    }
}

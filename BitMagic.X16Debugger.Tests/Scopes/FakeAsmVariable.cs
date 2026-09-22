using BitMagic.Common;

namespace BitMagic.X16Debugger.Tests.Scopes;

// Minimal IAsmVariable double: real implementations live in the compiler and pull in a scope
// tree, which is unnecessary weight for exercising the memory-offset/read/write extensions.
internal class FakeAsmVariable : IAsmVariable
{
    public string Name { get; init; } = "test";
    public int Value { get; init; }
    public VariableDataType VariableDataType { get; init; }
    public VariableType VariableType { get; init; } = VariableType.CompileConstant;
    public int Length { get; init; } = 1;
    public bool Array { get; init; }
    public bool RequiresReval { get; set; }
    public Func<bool, (int Value, bool RequiresReval)> Evaluate { get; init; } = _ => (0, false);
    public void Move(int offset) { }
}

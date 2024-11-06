using BitMagic.Common;
using BitMagic.X16Debugger.Variables;

namespace BitMagic.X16Debugger.Scopes;

internal static class IAsmVariableExtensions
{
    internal static Func<string> ToExpressionFunction(this DebuggerVariable variable, ExpressionManager expressionManager)
        => () => expressionManager.Evaluate(variable.Expression);

    internal static Func<string> ToStringFunction(this IAsmVariable variable, MemoryWrapper memory) =>
        variable.VariableDataType switch
        {
            VariableDataType.Constant => () => $"{variable.Value}",
            VariableDataType.ProcStart => () => $"0x{variable.Value:X4}",
            VariableDataType.ProcEnd => () => $"0x{variable.Value:X4}",
            VariableDataType.SegmentStart => () => $"0x{variable.Value:X4}",
            VariableDataType.LabelPointer => () => $"0x{variable.Value:X4}",
            VariableDataType.Byte => () => $"0x{memory[variable.Value].Byte:X2}",
            VariableDataType.Sbyte => () => $"{memory[variable.Value].Sbyte:0}",
            VariableDataType.Short => () => $"{memory[variable.Value].Short:0}",
            VariableDataType.Ushort => () => $"0x{memory[variable.Value].Ushort:X4}",
            VariableDataType.Int => () => $"{memory[variable.Value].Int:0}",
            VariableDataType.Uint => () => $"0x{memory[variable.Value].Uint:X8}",
            VariableDataType.Long => () => $"{memory[variable.Value].Long:0}",
            VariableDataType.Ulong => () => $"0x{memory[variable.Value].Ulong:X16}",
            VariableDataType.String => () => memory[variable.Value].String,
            VariableDataType.FixedStrings => () => memory[variable.Value].FixedString(variable.Length),
            _ => () => "Unhandled"
        };

    private static Func<string> ToStringValue(int value, int length, VariableDataType dataType, MemoryWrapper memory) =>
       dataType switch
       {
           VariableDataType.Constant => () => $"{value}",
           VariableDataType.ProcStart => () => $"0x{value:X4}",
           VariableDataType.ProcEnd => () => $"0x{value:X4}",
           VariableDataType.SegmentStart => () => $"0x{value:X4}",
           VariableDataType.LabelPointer => () => $"0x{value:X4}",
           VariableDataType.Byte => () => $"0x{memory[value].Byte:X2}",
           VariableDataType.Sbyte => () => $"{memory[value].Sbyte:0}",
           VariableDataType.Short => () => $"{memory[value].Short:0}",
           VariableDataType.Ushort => () => $"0x{memory[value].Ushort:X4}",
           VariableDataType.Int => () => $"{memory[value].Int:0}",
           VariableDataType.Uint => () => $"0x{memory[value].Uint:X8}",
           VariableDataType.Long => () => $"{memory[value].Long:0}",
           VariableDataType.Ulong => () => $"0x{memory[value].Ulong:X16}",
           VariableDataType.String => () => memory[value].String,
           VariableDataType.FixedStrings => () => memory[value].FixedString(length),
           _ => () => "Unhandled"
       };

    internal static object GetActualValue(this IAsmVariable variable, MemoryWrapper memory) =>
        variable.VariableDataType switch
        {
            VariableDataType.Constant => variable.Value,
            VariableDataType.ProcStart => (ushort)variable.Value,
            VariableDataType.ProcEnd => (ushort)variable.Value,
            VariableDataType.SegmentStart => (ushort)variable.Value,
            VariableDataType.LabelPointer => (ushort)variable.Value,
            VariableDataType.Byte => memory[variable.Value].Byte,
            VariableDataType.Sbyte => memory[variable.Value].Sbyte,
            VariableDataType.Short => memory[variable.Value].Short,
            VariableDataType.Ushort => memory[variable.Value].Ushort,
            VariableDataType.Int => memory[variable.Value].Int,
            VariableDataType.Uint => memory[variable.Value].Uint,
            VariableDataType.Long => memory[variable.Value].Long,
            VariableDataType.Ulong => memory[variable.Value].Ulong,
            VariableDataType.String => memory[variable.Value].String,
            VariableDataType.FixedStrings => memory[variable.Value].FixedString(variable.Length),
            _ => "Unhandled"
        };

    internal static string VariableTypeText(this IAsmVariable variable) =>
         variable.VariableDataType switch
         {
             VariableDataType.Byte => "byte",
             VariableDataType.Sbyte => "sbyte",
             VariableDataType.Short => "short",
             VariableDataType.Ushort => "ushort",
             VariableDataType.Int => "int",
             VariableDataType.Uint => "uint",
             VariableDataType.Long => "long",
             VariableDataType.Ulong => "ulong",
             VariableDataType.String => "string",
             VariableDataType.FixedStrings => "string",
             _ => "string"
         };
}
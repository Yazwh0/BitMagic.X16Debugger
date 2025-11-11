using BitMagic.Common;
using BitMagic.X16Debugger.Variables;
using System;

namespace BitMagic.X16Debugger.Scopes;

internal static class IAsmVariableExtensions
{
    internal static Func<string> ToExpressionFunction(this DebuggerVariable variable, ExpressionManager expressionManager)
        => () => expressionManager.Evaluate(variable.Expression);

    internal static int MemoryOffset(this IAsmVariable variable, int index) =>
        variable.Value + index * VariableDataTypeLength(variable);

    internal static Func<string> ToStringFunction(this IAsmVariable variable, MemoryWrapper memory, int index = 0) =>
        variable.VariableDataType switch
        {
            VariableDataType.Constant => () => $"{variable.Value}",
            VariableDataType.ProcStart => () => $"0x{variable.Value:X4}",
            VariableDataType.ProcEnd => () => $"0x{variable.Value:X4}",
            VariableDataType.SegmentStart => () => $"0x{variable.Value:X4}",
            VariableDataType.LabelPointer => () => $"0x{variable.Value:X4}",
            VariableDataType.Byte => () => $"0x{memory[MemoryOffset(variable, index)].Byte:X2}",
            VariableDataType.Sbyte => () => $"{memory[MemoryOffset(variable, index)].Sbyte:0}",
            VariableDataType.Short => () => $"{memory[MemoryOffset(variable, index)].Short:0}",
            VariableDataType.Ushort => () => $"0x{memory[MemoryOffset(variable, index)].Ushort:X4}",
            VariableDataType.Int => () => $"{memory[MemoryOffset(variable, index)].Int:0}",
            VariableDataType.Uint => () => $"0x{memory[MemoryOffset(variable, index)].Uint:X8}",
            VariableDataType.Long => () => $"{memory[MemoryOffset(variable, index)].Long:0}",
            VariableDataType.Ulong => () => $"0x{memory[MemoryOffset(variable, index)].Ulong:X16}",
            VariableDataType.String => () => memory[MemoryOffset(variable, index)].String,
            VariableDataType.FixedStrings => () => memory[MemoryOffset(variable, index)].FixedString(variable.Length),

            VariableDataType.Ptr => () => $"0x{memory[MemoryOffset(variable, index)].Ushort:X4}",

            VariableDataType.BytePtr => () => $"{memory[MemoryOffset(variable, index)].Ushort:X4} -> 0x{memory[memory[MemoryOffset(variable, index)].Ushort].Byte:X2}",
            VariableDataType.SbytePtr => () => $"0x{memory[MemoryOffset(variable, index)].Ushort:X4} -> {memory[memory[MemoryOffset(variable, index)].Ushort].Sbyte:0}",
            VariableDataType.ShortPtr => () => $"0x{memory[MemoryOffset(variable, index)].Ushort:X4} -> {memory[memory[MemoryOffset(variable, index)].Ushort].Short:0}",
            VariableDataType.UshortPtr => () => $"0x{memory[MemoryOffset(variable, index)].Ushort:X4} -> 0x{memory[memory[MemoryOffset(variable, index)].Ushort].Ushort:X4}",
            VariableDataType.IntPtr => () => $"0x{memory[MemoryOffset(variable, index)].Ushort:X4} -> {memory[memory[MemoryOffset(variable, index)].Ushort].Int:0}",
            VariableDataType.UintPtr => () => $"0x{memory[MemoryOffset(variable, index)].Ushort:X4} -> 0x{memory[memory[MemoryOffset(variable, index)].Ushort].Uint:X8}",
            VariableDataType.LongPtr => () => $"0x{memory[MemoryOffset(variable, index)].Ushort:X4} -> {memory[memory[MemoryOffset(variable, index)].Ushort].Long:0}",
            VariableDataType.UlongPtr => () => $"0x{memory[MemoryOffset(variable, index)].Ushort:X4} -> 0x{memory[memory[MemoryOffset(variable, index)].Ushort].Ulong:X16}",
            VariableDataType.StringPtr => () => $"0x{memory[MemoryOffset(variable, index)].Ushort:X4} -> {memory[memory[MemoryOffset(variable, index)].Ushort].String}",
            VariableDataType.FixedStringsPtr => () => $"0x{memory[MemoryOffset(variable, index)].Ushort:X4} -> {memory[memory[MemoryOffset(variable, index)].Ushort].FixedString(variable.Length)}",

            _ => () => "Unhandled"
        };

    //private static Func<string> ToStringValue(int value, int length, VariableDataType dataType, MemoryWrapper memory) =>
    //   dataType switch
    //   {
    //       VariableDataType.Constant => () => $"{value}",
    //       VariableDataType.ProcStart => () => $"0x{value:X4}",
    //       VariableDataType.ProcEnd => () => $"0x{value:X4}",
    //       VariableDataType.SegmentStart => () => $"0x{value:X4}",
    //       VariableDataType.LabelPointer => () => $"0x{value:X4}",
    //       VariableDataType.Byte => () => $"0x{memory[value].Byte:X2}",
    //       VariableDataType.Sbyte => () => $"{memory[value].Sbyte:0}",
    //       VariableDataType.Short => () => $"{memory[value].Short:0}",
    //       VariableDataType.Ushort => () => $"0x{memory[value].Ushort:X4}",
    //       VariableDataType.Int => () => $"{memory[value].Int:0}",
    //       VariableDataType.Uint => () => $"0x{memory[value].Uint:X8}",
    //       VariableDataType.Long => () => $"{memory[value].Long:0}",
    //       VariableDataType.Ulong => () => $"0x{memory[value].Ulong:X16}",
    //       VariableDataType.String => () => memory[value].String,
    //       VariableDataType.FixedStrings => () => memory[value].FixedString(length),
    //       _ => () => "Unhandled"
    //   };

    internal static object GetActualValue(this IAsmVariable variable, MemoryWrapper memory, int index = 0) =>
        variable.VariableDataType switch
        {
            VariableDataType.Constant => variable.Value,
            VariableDataType.ProcStart => (ushort)variable.Value,
            VariableDataType.ProcEnd => (ushort)variable.Value,
            VariableDataType.SegmentStart => (ushort)variable.Value,
            VariableDataType.LabelPointer => (ushort)variable.Value,
            VariableDataType.Byte => memory[MemoryOffset(variable, index)].Byte,
            VariableDataType.Sbyte => memory[MemoryOffset(variable, index)].Sbyte,
            VariableDataType.Short => memory[MemoryOffset(variable, index)].Short,
            VariableDataType.Ushort => memory[MemoryOffset(variable, index)].Ushort,
            VariableDataType.Int => memory[MemoryOffset(variable, index)].Int,
            VariableDataType.Uint => memory[MemoryOffset(variable, index)].Uint,
            VariableDataType.Long => memory[MemoryOffset(variable, index)].Long,
            VariableDataType.Ulong => memory[MemoryOffset(variable, index)].Ulong,
            VariableDataType.String => memory[MemoryOffset(variable, index)].String,
            VariableDataType.FixedStrings => memory[MemoryOffset(variable, index)].FixedString(variable.Length),

            VariableDataType.Ptr => memory[MemoryOffset(variable, index)].Ushort,

            VariableDataType.BytePtr => memory[memory[MemoryOffset(variable, index)].Ushort].Byte,
            VariableDataType.SbytePtr => memory[memory[MemoryOffset(variable, index)].Ushort].Sbyte,
            VariableDataType.ShortPtr => memory[memory[MemoryOffset(variable, index)].Ushort].Short,
            VariableDataType.UshortPtr => memory[memory[MemoryOffset(variable, index)].Ushort].Ushort,
            VariableDataType.IntPtr => memory[memory[MemoryOffset(variable, index)].Ushort].Int,
            VariableDataType.UintPtr => memory[memory[MemoryOffset(variable, index)].Ushort].Uint,
            VariableDataType.LongPtr => memory[memory[MemoryOffset(variable, index)].Ushort].Long,
            VariableDataType.UlongPtr => memory[memory[MemoryOffset(variable, index)].Ushort].Ulong,
            VariableDataType.StringPtr => memory[memory[MemoryOffset(variable, index)].Ushort].String,
            VariableDataType.FixedStringsPtr => memory[memory[MemoryOffset(variable, index)].Ushort].FixedString(variable.Length),

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

             VariableDataType.Ptr => "ptr",
             VariableDataType.BytePtr => "byte ptr",
             VariableDataType.SbytePtr => "sbyte ptr",
             VariableDataType.ShortPtr => "short ptr",
             VariableDataType.UshortPtr => "ushort ptr",
             VariableDataType.IntPtr => "int ptr",
             VariableDataType.UintPtr => "uint ptr",
             VariableDataType.LongPtr => "long ptr",
             VariableDataType.UlongPtr => "ulong ptr",
             VariableDataType.StringPtr => "string ptr",
             VariableDataType.FixedStringsPtr => "string ptr",

             _ => "string"
         };

    internal static int VariableDataTypeLength(this IAsmVariable variable) =>
        variable.VariableDataType switch
        {
            VariableDataType.Byte => 1,
            VariableDataType.Sbyte => 1,
            VariableDataType.Short => 2,
            VariableDataType.Ushort => 2,
            VariableDataType.Int => 4,
            VariableDataType.Uint => 4,
            VariableDataType.Long => 8,
            VariableDataType.Ulong => 8,
            VariableDataType.FixedStrings => variable.Length,
            VariableDataType.ProcStart => 2,

            VariableDataType.Ptr => 2,

            VariableDataType.BytePtr => 2,
            VariableDataType.SbytePtr => 2,
            VariableDataType.CharPtr => 2,
            VariableDataType.ShortPtr => 2,
            VariableDataType.UshortPtr => 2,
            VariableDataType.IntPtr => 2,
            VariableDataType.UintPtr => 2,
            VariableDataType.LongPtr => 2,
            VariableDataType.UlongPtr => 2,
            VariableDataType.StringPtr => 2,
            VariableDataType.FixedStringsPtr => 2,

            _ => 1
        };
}
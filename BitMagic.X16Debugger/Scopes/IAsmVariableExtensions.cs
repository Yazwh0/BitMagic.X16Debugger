using BitMagic.Common;
using BitMagic.X16Debugger.Variables;
using BitMagic.X16Emulator;
using System;
using System.Globalization;

namespace BitMagic.X16Debugger.Scopes;

internal static class IAsmVariableExtensions
{
    internal static Func<string> ToExpressionFunction(this DebuggerVariable variable, ExpressionManager expressionManager)
        => () => expressionManager.Evaluate(variable.Expression);

    internal static int MemoryOffset(this IAsmVariable variable, int index) =>
        variable.Value + index * VariableDataTypeLength(variable);

    // Only fixed-width numeric locations have an unambiguous single-value write - strings need
    // length/overflow handling and the *Ptr types display "address -> pointee", so leave both
    // read-only rather than guess which half a plain SetVariable value is meant to change.
    internal static bool SupportsDirectWrite(this IAsmVariable variable) =>
        variable.VariableDataType is
            VariableDataType.Byte or VariableDataType.Sbyte or
            VariableDataType.Short or VariableDataType.Ushort or
            VariableDataType.Int or VariableDataType.Uint or
            VariableDataType.Long or VariableDataType.Ulong or
            VariableDataType.Ptr;

    // Writes straight into the emulator's live memory (not a MemoryWrapper snapshot, which is a
    // .ToArray() copy). Accepts plain decimal, "0x"/"$"-prefixed hex - whatever VSC's edit box holds.
    internal static bool TrySetValue(this IAsmVariable variable, Emulator emulator, string text, int index = 0)
    {
        if (!variable.SupportsDirectWrite() || !TryParseInteger(text, out var parsed))
            return false;

        var memory = emulator.Memory;
        var offset = MemoryOffset(variable, index);

        switch (variable.VariableDataType)
        {
            case VariableDataType.Byte:
            case VariableDataType.Sbyte:
                memory[offset] = unchecked((byte)parsed);
                return true;
            case VariableDataType.Short:
                return BitConverter.TryWriteBytes(memory.Slice(offset, 2), unchecked((short)parsed));
            case VariableDataType.Ushort:
            case VariableDataType.Ptr:
                return BitConverter.TryWriteBytes(memory.Slice(offset, 2), unchecked((ushort)parsed));
            case VariableDataType.Int:
                return BitConverter.TryWriteBytes(memory.Slice(offset, 4), unchecked((int)parsed));
            case VariableDataType.Uint:
                return BitConverter.TryWriteBytes(memory.Slice(offset, 4), unchecked((uint)parsed));
            case VariableDataType.Long:
                return BitConverter.TryWriteBytes(memory.Slice(offset, 8), parsed);
            case VariableDataType.Ulong:
                return BitConverter.TryWriteBytes(memory.Slice(offset, 8), unchecked((ulong)parsed));
            default:
                return false;
        }
    }

    // Also reused directly by VariablesManager for the CPU register scope, which writes
    // straight to Emulator properties rather than through an IAsmVariable/memory offset.
    internal static bool TryParseInteger(string text, out long value)
    {
        text = text.Trim();

        // Hex parsing already reinterprets a full-width bit pattern (e.g. "FFFFFFFFFFFFFFFF" -> -1)
        // via two's complement, so Ulong's top half round-trips through the hex path unchanged.
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

        if (text.StartsWith("$", StringComparison.Ordinal))
            return long.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return true;

        // Decimal Ulong values above long.MaxValue don't fit a signed parse - reinterpret the bit pattern.
        if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var asUlong))
        {
            value = unchecked((long)asUlong);
            return true;
        }

        return false;
    }

    internal static Func<string> ToStringFunction(this IAsmVariable variable, MemoryWrapper memory, int index = 0)
    {
        try
        {
            return variable.VariableDataType switch
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
        } catch (Exception e)
        {
            return () => $"Error ({e.Message})";
        }
    }

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
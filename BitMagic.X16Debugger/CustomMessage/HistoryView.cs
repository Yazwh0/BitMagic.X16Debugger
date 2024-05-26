using BitMagic.Common;
using BitMagic.Common.Address;
using BitMagic.Decompiler;
using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.Extensions;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using System.Net;
using System.Text;

namespace BitMagic.X16Debugger.CustomMessage;

internal class HistoryRequest : DebugRequestWithResponse<HistoryRequestArguments, HistoryRequestResponse>
{
    public HistoryRequest() : base("getHistory")
    {
    }
}

internal static class HistoryRequestHandler
{
    public static HistoryRequestResponse HandleRequest(HistoryRequestArguments? arguments, Emulator emulator, SourceMapManager sourceMapManager, DebugableFileManager debugableFileManager)
    {
        var toReturn = new HistoryRequestResponse();

        var history = emulator.History;
        var idx = (int)emulator.HistoryPosition - 1;
        if (idx == -1)
            idx = 1023;

        for (var i = 0; i < 1000; i++)
        {
            if (history[idx].SP == 0 && history[idx].OpCode == 0 && history[idx].PC == 0)
                continue;

            var opCodeDef = OpCodes.GetOpcode(history[idx].OpCode);
            var opCode = "";
            var debuggerAddress = AddressFunctions.GetDebuggerAddress(history[idx].PC, history[idx].RamBank, history[idx].RomBank);
            IAsmVariable? variable = null;

            var instruction = sourceMapManager.GetSourceMap(debuggerAddress);
            var sourceFile = sourceMapManager.GetSourceFile(debuggerAddress);

            var sourceFilename = "";
            var lineNumber = -1;

            if (instruction != null && sourceFile != null && instruction.CanStep)
            {
                var wrapper = debugableFileManager.GetWrapper(sourceFile);

                if (wrapper != null)
                {
                    var binaryFile = wrapper.Source as IBinaryFile;

                    var (source, ln) = wrapper.FindUltimateSource(history[idx].PC - binaryFile.BaseAddress, debugableFileManager);

                    if (source != null)
                    {
                        sourceFilename = source.Path;
                        lineNumber = ln + 1;
                    }
                }
            }

            var proc = variable != null ? variable.Name : sourceMapManager.GetSymbol(debuggerAddress);

            var raw = Addressing.GetModeText(opCodeDef.AddressMode, history[idx].Params, history[idx].PC);

            if (opCodeDef.AddressMode == Addressing.AddressMode.Immediate || opCodeDef.AddressMode == Addressing.AddressMode.Implied || opCodeDef.AddressMode == Addressing.AddressMode.Accumulator || opCodeDef.AddressMode == Addressing.AddressMode.ZeroPageRelative)
            {
                opCode = $"{opCodeDef.OpCode.ToLower()} {raw}";
            }
            else
            {
                debuggerAddress = AddressFunctions.GetDebuggerAddress(history[idx].Params, history[idx].RamBank, history[idx].RomBank);

                var actValue = Addressing.GetModeValue(opCodeDef.AddressMode, debuggerAddress, history[idx].PC);

                string valueName = "";
                if (instruction != null && instruction.Scope.Variables.TryGetValue(debuggerAddress, instruction.Source, out variable))
                    valueName = variable?.Name ?? sourceMapManager.GetSymbol(actValue.Value);
                else
                    valueName = sourceMapManager.GetSymbol(actValue.Value);

                if (string.IsNullOrEmpty(valueName))
                    opCode = $"{opCodeDef.OpCode.ToLower()} {raw}";
                else
                    opCode = $"{opCodeDef.OpCode.ToLower()} {Addressing.GetModeText(opCodeDef.AddressMode, valueName, history[idx].PC)}";
            }

            toReturn.HistoryItems.Add(new HistoryItem(
                proc,
                opCode,
                raw,
                history[idx].RamBank,
                history[idx].RomBank,
                history[idx].PC,
                history[idx].A,
                history[idx].X,
                history[idx].Y,
                history[idx].SP,
                Flags(history[idx].Flags),
                sourceFilename,
                lineNumber));

            if (idx <= 0)
                idx = 1024;

            idx--;
        }

        return toReturn;
    }


    public static string Flags(byte flags)
    {
        var sb = new StringBuilder();

        sb.Append("[");

        if ((flags & (byte)CpuFlags.Negative) > 0)
            sb.Append("N");
        else
            sb.Append(" ");

        if ((flags & (byte)CpuFlags.Overflow) > 0)
            sb.Append("V");
        else
            sb.Append(" ");

        sb.Append(" "); // unused
        if ((flags & (byte)CpuFlags.Break) > 0)
            sb.Append("B");
        else
            sb.Append(" ");

        if ((flags & (byte)CpuFlags.Decimal) > 0)
            sb.Append("D");
        else
            sb.Append(" ");

        if ((flags & (byte)CpuFlags.InterruptDisable) > 0)
            sb.Append("I");
        else
            sb.Append(" ");

        if ((flags & (byte)CpuFlags.Zero) > 0)
            sb.Append("Z");
        else
            sb.Append(" ");

        if ((flags & (byte)CpuFlags.Carry) > 0)
            sb.Append("C");
        else
            sb.Append(" ");

        sb.Append("]");

        return sb.ToString();
    }

    [Flags]
    public enum CpuFlags : byte
    {
        None = 0,
        Carry = 1,
        Zero = 2,
        InterruptDisable = 4,
        Decimal = 8,
        Break = 16,
        Unused = 32,
        Overflow = 64,
        Negative = 128
    }
}

internal class HistoryRequestArguments : DebugRequestArguments
{
}

internal class HistoryRequestResponse : ResponseBody
{
    public List<HistoryItem> HistoryItems { get; set; } = new();
}

internal record class HistoryItem(string Proc, string OpCode, string RawParameter, int RamBank, int RomBank, int Pc, int A, int X, int Y, int Sp, string Flags, string SourceFile, int LineNumber);

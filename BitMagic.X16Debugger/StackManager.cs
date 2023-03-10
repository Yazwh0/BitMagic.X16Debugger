using BitMagic.Compiler;
using BitMagic.Compiler.Warnings;
using BitMagic.Decompiler;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace BitMagic.X16Debugger
{
    internal class StackManager
    {
        private const uint _interrupt = 0xffffffff;
        private const uint _nmi = 0xfffffffe;

        private readonly Emulator _emulator;
        private bool _invalid = true;
        private string _value = "";
        private readonly ICollection<Variable> _data = new List<Variable>();

        private readonly IdManager _idManager;
        private readonly List<StackFrame> _callStack = new List<StackFrame>();

        //private readonly Dictionary<int, SourceMap> _memoryToSourceMap;
        private readonly SourceMapManager _sourceMapManager;
        private readonly DisassemblerManager _dissassemblerManager;

        public StackManager(Emulator emulator, IdManager idManager, SourceMapManager sourceMapManager, DisassemblerManager disassemblerManager)
        {
            _emulator = emulator;
            _idManager = idManager;
            _sourceMapManager = sourceMapManager;
            _dissassemblerManager = disassemblerManager;
        }

        public (string Value, ICollection<Variable> Variables) GetStack()
        {
            if (_invalid)
                GenerateStackVariables();

            return (_value, _data);
        }

        private void GenerateStackVariables()
        {
            var count = 0x1ff - _emulator.StackPointer;

            if (count == 0)
            {
                _value = "No entries";
                _data.Clear();
                return;
            }

            if (count == 1)
                _value = $"1 entry";
            else
                _value = $"{count:##0} entries";

            _data.Clear();

            var stack = _emulator.Memory.Slice(_emulator.StackPointer + 1, 0x1ff - _emulator.StackPointer);

            for (var i = 0; i < stack.Length; i++)
                _data.Add(new Variable()
                {
                    Name = i.ToString(),
                    Type = "Byte",
                    PresentationHint = new VariablePresentationHint() { Kind = VariablePresentationHint.KindValue.Data },
                    Value = $"0x{stack[i]:X2}",
                });

            _invalid = false;
        }

        public void Invalidate()
        {
            _invalid = true;
        }

        public IEnumerable<StackFrame> GetCallStack => _callStack;

        // data stored in the stack info has the ram\rom bank switched to how the debugger address.
        public void GenerateCallStack()
        {
            _callStack.Clear();

            var frame = GenerateFrame("> ", _emulator.Pc, _emulator.Memory[0x00], _emulator.Memory[0x01], (_emulator.StackPointer + 1) & 0xff, false);

            _callStack.Add(frame);

            // walk down the stack looking for JSR, interrupts, breaks and nmi
            var sp = _emulator.StackPointer & 0xff;
            sp++;
            uint prev = 0;
            while (sp < 0x100)
            {
                var stackInfo = _emulator.StackInfo[sp];
                sp++;

                if (prev == stackInfo || stackInfo == 0)
                    continue;

                prev = stackInfo;

                // todo: add code reference to what was interrupted
                if (stackInfo == _interrupt || stackInfo == _nmi)
                {
                    var returnAddress = _emulator.Memory[0x100 + sp] + (_emulator.Memory[0x100 + sp + 1] << 8) - 2;
                    var bankInfo  = _emulator.StackInfo[sp] >> 16;
                    var iramBank = bankInfo & 0xff;
                    var iromBank = (bankInfo & 0xff00) >> 8;
                    sp++;

                    _callStack.Add(GenerateFrame(stackInfo == _interrupt ? "INT: " : "NMI: ", returnAddress, (int)iramBank, (int)iromBank, -1, true));
                    continue;
                }

                var (opCode, address, ramBank, romBank) = GetOpCode(stackInfo);

                if (opCode != 0x20 && opCode != 0x00)
                    continue;

                frame = GenerateFrame("", address, ramBank, romBank, sp-1, true);

                _callStack.Add(frame);
            }
        }

        private StackFrame GenerateFrame(string prefix, int address, int ramBank, int romBank, int stackPointer, bool checkReturnAddress)
        {
            // return address is adjusted here to give the location of the jsr. need to +3 for the correct return address.
            var returnAddress = _emulator.Memory[0x100 + stackPointer] + (_emulator.Memory[0x100 + stackPointer + 1] << 8) - 2;
            var addressString = AddressFunctions.GetDebuggerAddressDisplayString(address, ramBank, romBank);

            if (checkReturnAddress && stackPointer != -1 && (address & 0xffff) != returnAddress)
            {
                addressString = $"({addressString} -> {returnAddress + 3:X4})";
            }
            else
            {
                addressString = $"({addressString})";
            }

            var frame = new StackFrame();
            frame.Id = _idManager.GetId();
            frame.InstructionPointerReference = AddressFunctions.GetDebuggerAddressString(address, ramBank, romBank);

            var debuggerAddress = AddressFunctions.GetDebuggerAddress(address, ramBank, romBank);

            var instruction = _sourceMapManager.GetSourceMap(debuggerAddress);
            if (instruction != null)
            {
                var line = instruction.Line as Line;
                if (line == null)
                    frame.Name = $"{prefix}??? {addressString} (Data?)";
                else
                    frame.Name = $"{prefix}{line.Procedure.Name} {addressString}";

                frame.Line = instruction.Line.Source.LineNumber;
                frame.Source = new Source()
                {
                    Name = Path.GetFileName(instruction.Line.Source.Name),
                    Path = instruction.Line.Source.Name,
                };
            }
            else
            {
                //// hunt backward for a symbol
                var thisAddress = debuggerAddress;
                string? huntSymbol = null;
                for (var i = 0; i < 256; i++) // 256 is a bit arbitary...?
                {
                    if ((thisAddress & 0xff0000) != ((thisAddress - 1) & 0xff0000))
                    {
                        break;
                    }

                    huntSymbol = _sourceMapManager.GetSymbol(thisAddress);
                    if (!string.IsNullOrWhiteSpace(huntSymbol))
                        break;

                    thisAddress--;
                }

                huntSymbol = string.IsNullOrWhiteSpace(huntSymbol) ? "???" : $"{huntSymbol} +0x{debuggerAddress - thisAddress:X2}"; // todo adjust 

                frame.Name = $"{prefix}{huntSymbol} {addressString}";
                var (source, lineNumber) = GetSource(address, ramBank, romBank);
                frame.Source = source;
                frame.Line = lineNumber;
            }

            return frame;
        }

        private (Source? Source, int LineNumber) GetSource(int address, int ramBank, int romBank)
        {
            var sourceId = _dissassemblerManager.GetDisassemblyId(address, ramBank, romBank);
            if (sourceId != 0)
            {
                var sourceFile = _idManager.GetObject<DecompileReturn>(sourceId);
                if (sourceFile != null)
                {
                    var lineNumber = 0;
                    foreach(var i in sourceFile.Items.Where(i => i.Value.HasInstruction))
                    {
                        if (i.Value.Address >= address)
                        {
                            lineNumber = i.Key;
                            break;
                        }
                    }

                    return (new Source()
                    {
                        Name = sourceFile.Name,
                        Path = sourceFile.Path,
                        Origin = sourceFile.Origin,
                        SourceReference = sourceFile.ReferenceId
                    }, lineNumber);
                }
            }

            return (null, 0);
        }

        private (byte OpCode, int Address, int RamBank, int RomBank) GetOpCode(uint stackInfo)
        {
            var rawAddress = stackInfo & 0xffff;

            if (rawAddress >= 0xc000)
            {
                var romBank = (int)((stackInfo & 0xff000000) >> 24);
                return (_emulator.RomBank[romBank * 0x4000 + (int)rawAddress - 0xc000], (int)rawAddress, 0, romBank);
            }

            if (rawAddress >= 0xa000)
            {
                var ramBank = (int)((stackInfo & 0xff0000) >> 16);
                return (_emulator.RomBank[ramBank * 0x2000 + (int)rawAddress - 0xa000], (int)rawAddress, ramBank, 0);

            }

            return (_emulator.Memory[(int)rawAddress], (int)rawAddress, 0, 0);
        }
    }
}

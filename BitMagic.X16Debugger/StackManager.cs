using BitMagic.Compiler;
using BitMagic.Compiler.Warnings;
using BitMagic.Decompiler;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
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

        public void GenerateCallStack()
        {
            _callStack.Clear();

            // current position
            var frame = new StackFrame();
            frame.Id = _idManager.GetId();
            var pc = _emulator.Pc;

            var addressString = AddressFunctions.GetDebuggerAddressDisplayString(pc, _emulator.Memory[0x00], _emulator.Memory[0x01]);
            var debuggerAddress = AddressFunctions.GetDebuggerAddress(pc, _emulator.Memory[0x00], _emulator.Memory[0x01]);

            var instruction = _sourceMapManager.GetSourceMap(debuggerAddress);

            if (instruction != null)
            {
                var line = instruction.Line as Line;
                if (line == null)
                    frame.Name = $"Current: {addressString}";
                else
                    frame.Name = $"Current: {line.Procedure.Name} {addressString}";

                frame.Line = instruction.Line.Source.LineNumber;
                frame.InstructionPointerReference = AddressFunctions.GetDebuggerAddressString(pc, _emulator.Memory[0x00], _emulator.Memory[0x01]);
                frame.Source = new Source()
                {
                    Name = Path.GetFileName(instruction.Line.Source.Name),
                    Path = instruction.Line.Source.Name,
                };
            }
            else
            {
                frame.Name = $"??? Current: {addressString}";
                frame.InstructionPointerReference = AddressFunctions.GetDebuggerAddressString(pc, _emulator.Memory[0x00], _emulator.Memory[0x01]);

                var (source, lineNumber) = GetSource(pc, _emulator.Memory[0x00], _emulator.Memory[0x01]);
                frame.Source = source;
                frame.Line = lineNumber;
            }

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
                    frame = new StackFrame();
                    frame.Id = _idManager.GetId();
                    frame.Name = stackInfo == _interrupt ? "INTERRUPT" : "NMI";
                    _callStack.Add(frame);
                    continue;
                }

                var (opCode, address, ramBank, romBank) = GetOpCode(stackInfo);

                if (opCode != 0x20 && opCode != 0x00)
                    continue;

                addressString = AddressFunctions.GetDebuggerAddressDisplayString(address, ramBank, romBank);

                frame = new StackFrame();
                frame.Id = _idManager.GetId();
                frame.InstructionPointerReference = AddressFunctions.GetDebuggerAddressString(address, ramBank, romBank);

                debuggerAddress = AddressFunctions.GetDebuggerAddress(address, ramBank, romBank);

                instruction = _sourceMapManager.GetSourceMap(debuggerAddress);
                if (instruction != null)
                {
                    var line = instruction.Line as Line;
                    if (line == null)
                        frame.Name = $"??? From: {addressString} (Data?)";
                    else
                        frame.Name = $"{line.Procedure.Name} {addressString}";

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

                    frame.Name = $"{huntSymbol} ({addressString})";
                    var (source, lineNumber) = GetSource(address, ramBank, romBank);
                    frame.Source = source;
                    frame.Line = lineNumber;
                }

                _callStack.Add(frame);
            }
        }

        private (Source? Source, int LineNumber) GetSource(int address, int ramBank, int romBank)
        {
            var sourceId = _dissassemblerManager.GetDisassembleyId(address, ramBank, romBank);
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
                var romBank = (int)((stackInfo & 0xff0000) >> 16);
                return (_emulator.RomBank[romBank * 0x4000 + (int)rawAddress - 0xc000], (int)rawAddress, 0, romBank);
            }

            if (rawAddress >= 0xa000)
            {
                var ramBank = (int)((stackInfo & 0xff000000) >> 24);
                return (_emulator.RomBank[ramBank * 0x2000 + (int)rawAddress - 0xa000], (int)rawAddress, ramBank, 0);

            }

            return (_emulator.Memory[(int)rawAddress], (int)rawAddress, 0, 0);
        }
    }
}

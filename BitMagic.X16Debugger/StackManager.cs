using BitMagic.Common;
using BitMagic.Common.Address;
using BitMagic.Compiler;
using BitMagic.Decompiler;
using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.Extensions;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;


namespace BitMagic.X16Debugger;

internal class StackManager
{
    private const uint _interrupt = 0xffffffff;
    private const uint _nmi = 0xfffffffe;

    private readonly Emulator _emulator;
    private bool _invalid = true;
    private string _value = "";
    private readonly ICollection<Variable> _data = new List<Variable>();

    private readonly IdManager _idManager;
    private readonly List<StackFrameState> _callStack = new List<StackFrameState>();

    private readonly SourceMapManager _sourceMapManager;
    private readonly DisassemblerManager _dissassemblerManager;
    private readonly DebugableFileManager _debugableFileManager;

    public StackManager(Emulator emulator, IdManager idManager, SourceMapManager sourceMapManager, DisassemblerManager disassemblerManager, DebugableFileManager debugableFileManager)
    {
        _emulator = emulator;
        _idManager = idManager;
        _sourceMapManager = sourceMapManager;
        _dissassemblerManager = disassemblerManager;
        _debugableFileManager = debugableFileManager;
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
                Type = "byte",
                PresentationHint = new VariablePresentationHint() { Kind = VariablePresentationHint.KindValue.Data },
                Value = $"0x{stack[i]:X2}",
            });

        _invalid = false;
    }

    public void Invalidate()
    {
        _invalid = true;
    }

    public IEnumerable<StackFrameState> CallStack => _callStack;

    // data stored in the stack info has the ram\rom bank switched to how the debugger address.
    public void GenerateCallStack()
    {
        _callStack.Clear();

        var frame = GenerateFrame(new StackItem("> ", _emulator.Pc, _emulator.Memory[0x00], _emulator.Memory[0x01], (_emulator.StackPointer + 1) & 0xff, false));

        _callStack.Add(frame);
        _callStack.AddRange(GenerateFrames().Select(i => GenerateFrame(i)));
    }

    private IEnumerable<StackItem> GenerateFrames()
    {
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

            if (stackInfo == _interrupt || stackInfo == _nmi)
            {
                var returnAddress = _emulator.Memory[0x100 + sp] + (_emulator.Memory[0x100 + sp + 1] << 8) - 2;
                var bankInfo = _emulator.StackInfo[sp] >> 16;
                var iramBank = bankInfo & 0xff;
                var iromBank = (bankInfo & 0xff00) >> 8;
                sp++;
                yield return new StackItem(stackInfo == _interrupt ? "INT: " : "NMI: ", returnAddress, (int)iramBank, (int)iromBank, -1, true);
                continue;
            }

            var (opCode, address, ramBank, romBank) = GetOpCode(stackInfo);

            if (opCode != 0x20 && opCode != 0x00)
                continue;

            yield return new StackItem("", address, ramBank, romBank, sp - 1, true);
        }
    }

    // Sets a breakpoint on the caller on the stack breakpoint list. If no called can be found, it will do nothing.
    public void SetBreakpointOnCaller()
    {
        var first = GenerateFrames().FirstOrDefault();

        if (first == null)
            return;

        _emulator.StackBreakpoints[first.StackPointer + 1] = 0x01;
    }

    private StackFrameState GenerateFrame(StackItem parameters)
    {
        var (prefix, address, ramBank, romBank, stackPointer, checkReturnAddress) = parameters;

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
        var toReturn = new StackFrameState(frame);
        frame.Id = _idManager.GetId(); // ???
        frame.InstructionPointerReference = AddressFunctions.GetDebuggerAddressString(address, ramBank, romBank);

        var debuggerAddress = AddressFunctions.GetDebuggerAddress(address, ramBank, romBank);

        var instruction = _sourceMapManager.GetSourceMap(debuggerAddress);

        ///// NEW
        if (instruction != null) // this is a bitmagic procedure, instruction is from the generated bmasm.
        {
            var sourceFile = _sourceMapManager.GetSourceFile(debuggerAddress);
            if (sourceFile != null)
            {
                // need to find the intermediary file, not the end file so we can pull the procedure name
                var wrapper = _debugableFileManager.GetWrapper(sourceFile);

                if (wrapper != null)
                {
                    var binaryFile = wrapper.Source as IBinaryFile;

                    // this is the correct line
                    
                    if (!instruction.CanStep)
                    {
                        frame.Name = $"{prefix}??? {addressString} (Data)";

                        // set the source to memory
                        var (memorySource, memorylineNumber) = GetSource(address, ramBank, romBank);

                        frame.Source = memorySource;
                        frame.Line = memorylineNumber;
                        return toReturn;
                    }

                    frame.Name = $"{prefix}{instruction.Scope.Name} {addressString}";
                    toReturn.Scope = instruction.Scope;

                    var (source, lineNumber) = wrapper.FindUltimateSource(address - binaryFile.BaseAddress, _debugableFileManager);

                    if (source != null)
                    {
                        frame.Source = source.AsSource();
                        frame.Line = lineNumber + 1;
                        return toReturn;
                    }
                }
            }
        }
        else
        {
            /////

            //if (instruction != null)
            //{
            //    var line = instruction.Line as Line; // this is the correct line
            //    if (line == null)
            //    {
            //        frame.Name = $"{prefix}??? {addressString} (Data)";
            //        var (memorySource, memorylineNumber) = GetSource(address, ramBank, romBank);

            //        frame.Source = memorySource;
            //        frame.Line = memorylineNumber;
            //        return toReturn;
            //    }
            //    else
            //    {
            //        frame.Name = $"{prefix}{line.Procedure.Name} {addressString}";
            //        toReturn.Line = line;
            //    }

            //    var source = line.Source.SourceFile;
            //    var lineNumber = instruction.Line.Source.LineNumber - 1; // what comes out of bitmagic is 1 based.
            //    if (line.Source.SourceFile != null && source is ProcessResult)
            //    {   // we're in a mapped file, find the actual line in the source
            //        var mappedSource = source as ProcessResult; // process result appears to be out by 1 for libraries
            //        source = new BitMagicProjectFile(mappedSource.Source.Map[lineNumber].SourceFilename);
            //        lineNumber = mappedSource.Source.Map[lineNumber].Line + 1;
            //    }

            //    frame.Line = lineNumber;
            //    frame.Source = new Source()
            //    {
            //        Name = Path.GetFileName(source.Name),
            //        Path = source.Path,
            //        SourceReference = source.ReferenceId,
            //        Origin = source.Origin.ToString()
            //    };
            //}
            //else
            //{
            //// hunt backward for a symbol6
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
            //}
        }

        return toReturn;
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
                if (!sourceFile.Items.Any())
                {
                    sourceFile.GetContent(); // force disassembly
                }

                foreach (var i in sourceFile.Items.Where(i => i.Value.HasInstruction))
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
                    Origin = sourceFile.Origin.ToString(),
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

internal record StackItem(string Prefix, int Address, int RamBank, int RomBank, int StackPointer, bool CheckReturnAddress);

public class StackFrameState
{
    public StackFrame StackFrame { get; }
    public IScope? Scope { get; internal set; } = null;

    public StackFrameState(StackFrame stackFrame)
    {
        StackFrame = stackFrame;
    }
}
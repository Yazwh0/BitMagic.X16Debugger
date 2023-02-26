using BitMagic.Common;
using BitMagic.Compiler;
using BitMagic.X16Emulator;
using BitMagic.X16Emulator.Display;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Utilities;
using Newtonsoft.Json;
using System.Text;
using SysThread = System.Threading.Thread;

namespace BitMagic.X16Debugger;

public class X16Debug : DebugAdapterBase
{
    private readonly Func<Emulator> _getNewEmulatorInstance;
    private Emulator _emulator;

    private SysThread? _debugThread;
    private SysThread? _windowThread;

    // managers
    private readonly ExceptionManager _exceptionManager;
    private readonly BreakpointManager _breakpointManager;
    private readonly ScopeManager _scopeManager;
    private readonly VariableManager _variableManager;
    private readonly StackManager _stackManager;

    public Dictionary<int, SourceMap> MemoryToSourceMap { get; } = new();
    public Dictionary<string, HashSet<CodeMap>> SourceToMemoryMap { get; } = new();

    private bool _running = true;

    private ManualResetEvent _runEvent = new ManualResetEvent(false);
    private object SyncObject = new object();

    // This will be started on a second thread, seperate to the emulator
    public X16Debug(Func<Emulator> getNewEmulatorInstance, Stream stdIn, Stream stdOut)
    {
        _getNewEmulatorInstance = getNewEmulatorInstance;
        _emulator = getNewEmulatorInstance();

        var idManager = new IdManager();

        _exceptionManager = new ExceptionManager(this);
        _breakpointManager = new BreakpointManager(_emulator, this);
        _scopeManager = new ScopeManager(idManager);
        _variableManager = new VariableManager(idManager);
        _stackManager = new StackManager(_emulator);

        SetupGlobalObjects();

        InitializeProtocolClient(stdIn, stdOut);

        Protocol.RequestReceived += Protocol_RequestReceived;
        Protocol.RequestCompleted += Protocol_RequestCompleted;
        Protocol.LogMessage += Protocol_LogMessage;
    }

    private void Protocol_LogMessage(object? sender, LogEventArgs e)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("LOG ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(JsonConvert.SerializeObject(e));
        Console.ResetColor();
    }

    private void Protocol_RequestCompleted(object? sender, RequestCompletedEventArgs e)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("CMP ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(JsonConvert.SerializeObject(e));
        Console.ResetColor();
    }

    private void Protocol_RequestReceived(object? sender, RequestReceivedEventArgs e)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("RCV ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(JsonConvert.SerializeObject(e));
        Console.ResetColor();
    }

    private void SetupGlobalObjects()
    {
        var scope = _scopeManager.GetScope("CPU", false);

        scope.AddVariable(
            _variableManager.Register(
                new VariableChildren("Flags", "Flags",
                () => $"[{(_emulator.Negative ? "N" : " ")}{(_emulator.Overflow ? "V" : " ")} {(_emulator.BreakFlag ? "B" : " ")}{(_emulator.Decimal ? "D" : " ")}{(_emulator.InterruptDisable ? "I" : " ")}{(_emulator.Zero ? "Z" : " ")}{(_emulator.Carry ? "C" : " ")}]",
                new[] {
                    new VariableMap("Negative", "Bool", () => _emulator.Negative.ToString(), attribute: VariablePresentationHint.AttributesValue.IsBoolean),
                    new VariableMap("Overflow", "Bool", () => _emulator.Overflow.ToString(), attribute: VariablePresentationHint.AttributesValue.IsBoolean),
                    new VariableMap("Break", "Bool", () => _emulator.BreakFlag.ToString(), attribute: VariablePresentationHint.AttributesValue.IsBoolean),
                    new VariableMap("Decimal", "Bool", () => _emulator.Decimal.ToString(), attribute: VariablePresentationHint.AttributesValue.IsBoolean),
                    new VariableMap("Interupt", "Bool", () => _emulator.InterruptDisable.ToString(), attribute: VariablePresentationHint.AttributesValue.IsBoolean),
                    new VariableMap("Zero", "Bool", () => _emulator.Zero.ToString(), attribute: VariablePresentationHint.AttributesValue.IsBoolean),
                    new VariableMap("Carry", "Bool", () => _emulator.Carry.ToString(), attribute: VariablePresentationHint.AttributesValue.IsBoolean),
                })));
        scope.AddVariable(new VariableMap("A", "Byte", () => $"0x{_emulator.A:X2}"));
        scope.AddVariable(new VariableMap("X", "Byte", () => $"0x{_emulator.X:X2}"));
        scope.AddVariable(new VariableMap("Y", "Byte", () => $"0x{_emulator.Y:X2}"));
        scope.AddVariable(new VariableMap("PC", "Word", () => $"0x{_emulator.Pc:X4}"));
        scope.AddVariable(new VariableMap("SP", "Byte", () => $"0x{_emulator.StackPointer:X2}"));
        scope.AddVariable(
            _variableManager.Register(
                new VariableIndex("Stack", _stackManager.GetStack)));

        scope.AddVariable(
            _variableManager.Register(
                new VariableChildren("RAM", "Byte[]", () => $"0x10000 bytes",
                new IVariableMap[]
                {
                    new VariableMap("Ram Bank", "Byte", () => $"0x{_emulator.Memory[0]:X2}"),
                    new VariableMap("Rom Bank", "Byte", () => $"0x{_emulator.Memory[1]:X2}"),
                    new VariableMemory("RAM", "main", () => "Data")
                })));

        scope = _scopeManager.GetScope("VERA", false);

        scope.AddVariable(
            _variableManager.Register(
                new VariableChildren("Data 0", "Byte", () => $"0x{_emulator.Memory[0x9F23]:X2}",
                new[] {
                    new VariableMap("Address", "DWord", () => $"0x{_emulator.Vera.Data0_Address:X5}"),
                    new VariableMap("Step", "Byte", () => $"0x{_emulator.Vera.Data0_Step:X2}")
                }
            )));

        scope.AddVariable(
            _variableManager.Register(
                new VariableChildren("Data 1", "Byte", () => $"0x{_emulator.Memory[0x9F24]:X2}",
                new[] {
                    new VariableMap("Address", "DWord", () => $"0x{_emulator.Vera.Data1_Address:X5}"),
                    new VariableMap("Step", "Byte", () => $"0x{_emulator.Vera.Data1_Step:X2}")
                }
            )));

        scope.AddVariable(new VariableMemory("VRAM", "vram", () => "Data"));

        scope = _scopeManager.GetScope("Kernal", false);

        scope.AddVariable(new VariableMap("R0", "Word", () => $"0x{_emulator.Memory[0x02] + (_emulator.Memory[0x03] << 8):X4}"));
        scope.AddVariable(new VariableMap("R1", "Word", () => $"0x{_emulator.Memory[0x04] + (_emulator.Memory[0x05] << 8):X4}"));
        scope.AddVariable(new VariableMap("R2", "Word", () => $"0x{_emulator.Memory[0x06] + (_emulator.Memory[0x07] << 8):X4}"));
        scope.AddVariable(new VariableMap("R3", "Word", () => $"0x{_emulator.Memory[0x08] + (_emulator.Memory[0x09] << 8):X4}"));

        scope.AddVariable(new VariableMap("R4", "Word", () => $"0x{_emulator.Memory[0x0a] + (_emulator.Memory[0x0b] << 8):X4}"));
        scope.AddVariable(new VariableMap("R5", "Word", () => $"0x{_emulator.Memory[0x0c] + (_emulator.Memory[0x0d] << 8):X4}"));
        scope.AddVariable(new VariableMap("R6", "Word", () => $"0x{_emulator.Memory[0x0e] + (_emulator.Memory[0x0f] << 8):X4}"));
        scope.AddVariable(new VariableMap("R7", "Word", () => $"0x{_emulator.Memory[0x10] + (_emulator.Memory[0x11] << 8):X4}"));

        scope.AddVariable(new VariableMap("R8", "Word", () => $"0x{_emulator.Memory[0x12] + (_emulator.Memory[0x13] << 8):X4}"));
        scope.AddVariable(new VariableMap("R9", "Word", () => $"0x{_emulator.Memory[0x14] + (_emulator.Memory[0x15] << 8):X4}"));
        scope.AddVariable(new VariableMap("R10", "Word", () => $"0x{_emulator.Memory[0x16] + (_emulator.Memory[0x17] << 8):X4}"));
        scope.AddVariable(new VariableMap("R11", "Word", () => $"0x{_emulator.Memory[0x18] + (_emulator.Memory[0x19] << 8):X4}"));

        scope.AddVariable(new VariableMap("R12", "Word", () => $"0x{_emulator.Memory[0x1a] + (_emulator.Memory[0x1b] << 8):X4}"));
        scope.AddVariable(new VariableMap("R13", "Word", () => $"0x{_emulator.Memory[0x1c] + (_emulator.Memory[0x1d] << 8):X4}"));
        scope.AddVariable(new VariableMap("R14", "Word", () => $"0x{_emulator.Memory[0x1e] + (_emulator.Memory[0x1f] << 8):X4}"));
        scope.AddVariable(new VariableMap("R15", "Word", () => $"0x{_emulator.Memory[0x20] + (_emulator.Memory[0x21] << 8):X4}"));
    }

    public SysThread? DebugThread => _debugThread;

    /// <summary>
    /// Runs the adaptor, and returns once debugging is over.
    /// </summary>
    public void Run()
    {
        this.Protocol.Run();
        this.Protocol.WaitForReader();
    }

    private void Emulate()
    {
        EmulatorWork.Emulator = _emulator;
        var emulatorThread = new System.Threading.Thread(EmulatorWork.DoWork);
        emulatorThread.Priority = ThreadPriority.Highest;
        emulatorThread.Start();

        EmulatorWindow.Run(_emulator);
        emulatorThread.Join();
    }

    #region Initialize/Disconnect

    protected override InitializeResponse HandleInitializeRequest(InitializeArguments arguments)
    {
        //       if (arguments.LinesStartAt1 == true)
        //            this.clientsFirstLine = 1;

        this.Protocol.SendEvent(new InitializedEvent());

        return new InitializeResponse()
        {
            SupportsReadMemoryRequest = true,
            //SupportsEvaluateForHovers = true,
            //SupportsExceptionOptions = true,
            //SupportsConfigurationDoneRequest = true
        };
    }

    protected override LaunchResponse HandleLaunchRequest(LaunchArguments arguments)
    {
        var toCompile = arguments.ConfigurationProperties.GetValueAsString("program");

        if (!File.Exists(toCompile))
        {
            throw new ProtocolException($"Launch failed because '{toCompile}' does not exist.");
        }

        Console.WriteLine($"Compiling {toCompile}");

        try
        {
            var project = new Project();
            project.Code.Load(toCompile).GetAwaiter().GetResult();
            var compiler = new Compiler.Compiler(project);

            var compileResult = compiler.Compile().GetAwaiter().GetResult();

            ConstructSourceMap(compileResult);

            if (compileResult.Warnings.Any())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Warnings:");
                foreach (var warning in compileResult.Warnings)
                {
                    Console.WriteLine(warning);
                }
                Console.ResetColor();
            }

            var prg = compileResult.Data["Main"].ToArray();
            var destAddress = 0x801;
            for (var i = 2; i < prg.Length; i++)
            {
                _emulator.Memory[destAddress++] = prg[i];
            }
            _emulator.Pc = 0x801;
            Console.WriteLine($"Done. {prg.Length:#,##0} bytes. Starting at 0x801");
        }
        catch (Exception e)
        {
            throw new ProtocolException(e.Message);
        }

        _running = true;
        _debugThread = new SysThread(DebugLoop);
        _debugThread.Name = "DebugLoop Thread";
        _debugThread.Start();

        _windowThread = new SysThread(() => EmulatorWindow.Run(_emulator));
        _windowThread.Name = "Debugger Window";
        _windowThread.Start();

        return new LaunchResponse();
    }

    protected override DisconnectResponse HandleDisconnectRequest(DisconnectArguments arguments)
    {
        _breakpointManager.Clear();
        MemoryToSourceMap.Clear();
        EmulatorWindow.Stop();
        _emulator = _getNewEmulatorInstance();
        return new DisconnectResponse();
    }

    #endregion

    #region Breakpoints

    protected override SetBreakpointsResponse HandleSetBreakpointsRequest(SetBreakpointsArguments arguments)
        => _breakpointManager.HandleSetBreakpointsRequest(arguments);

    protected override SetExceptionBreakpointsResponse HandleSetExceptionBreakpointsRequest(SetExceptionBreakpointsArguments arguments)
       => _exceptionManager.HandleSetExceptionBreakpointsRequest(arguments);

    #endregion

    #region Continue/Stepping

    // Startup
    protected override ConfigurationDoneResponse HandleConfigurationDoneRequest(ConfigurationDoneArguments arguments)
    {
        return new ConfigurationDoneResponse();
    }

    protected override ContinueResponse HandleContinueRequest(ContinueArguments arguments)
    {
        _emulator.Stepping = false;
        lock (SyncObject)
        {
            _runEvent.Set();
        }

        return new ContinueResponse();
    }

    protected override StepInResponse HandleStepInRequest(StepInArguments arguments)
    {
        return new StepInResponse();
    }

    protected override StepOutResponse HandleStepOutRequest(StepOutArguments arguments)
    {
        return new StepOutResponse();
    }


    protected override NextResponse HandleNextRequest(NextArguments arguments)
    {
        _emulator.Stepping = true;
        lock (SyncObject)
        {
            _runEvent.Set();
        }

        return new NextResponse();
    }

    #endregion

    #region Debug Loop

    // Run the emulator, handle stops, breakpoints, etc.
    // This is running in _debugThread.
    private void DebugLoop()
    {
        Console.WriteLine("Starting emulator");
        while (_running)
        {
            var returnCode = _emulator.Emulate();

            switch (returnCode)
            {
                case Emulator.EmulatorResult.Stepping:
                    this.Protocol.SendEvent(new StoppedEvent(StoppedEvent.ReasonValue.Step, "Stepping", 0, null, true));
                    _emulator.Stepping = true;
                    break;
                case Emulator.EmulatorResult.DebugOpCode:
                    this.Protocol.SendEvent(new StoppedEvent(StoppedEvent.ReasonValue.Breakpoint));
                    _emulator.Stepping = true;
                    break;
                case Emulator.EmulatorResult.Breakpoint:
                    this.Protocol.SendEvent(new StoppedEvent(StoppedEvent.ReasonValue.Breakpoint, "Breakpoint hit", 0, null, true));
                    _emulator.Stepping = true;
                    break;
                default:
                    Console.WriteLine($"Stopping with because of {returnCode} result");
                    this.Protocol.SendEvent(new ExitedEvent((int)returnCode));
                    this.Protocol.SendEvent(new TerminatedEvent());
                    _running = false;
                    return;
            }

            // todo: only send update events if something has changed.
            this.Protocol.SendEvent(new MemoryEvent() { MemoryReference = "vram", Offset = 0, Count = 0x20000 });
            this.Protocol.SendEvent(new MemoryEvent() { MemoryReference = "main", Offset = 0, Count = 0x10000 });

            _runEvent.WaitOne(); // wait for a signal to continue
            lock (SyncObject)
            {
                _runEvent.Reset();
            }

            _stackManager.Invalidate();
        }
    }

    #endregion

    #region Inspection

    //        if (!this.stopped)
    //        {
    //            throw new ProtocolException("Not in break mode!");
    //        }
    protected override ThreadsResponse HandleThreadsRequest(ThreadsArguments arguments)
        => new ThreadsResponse(new List<Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Thread>
            {
                new Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages.Thread()
                {
                    Id = 0,
                    Name = "CX16"
                }
            });

    #endregion

    #region Memory

    protected override ReadMemoryResponse HandleReadMemoryRequest(ReadMemoryArguments arguments)
    {
        var toReturn = new ReadMemoryResponse();

        Span<byte> data;
        switch (arguments.MemoryReference)
        {
            case "main":
                data = _emulator.Memory;
                break;
            case "vram":
                data = _emulator.Vera.Vram;
                break;
            default:
                throw new Exception($"Unknown memory reference {arguments.MemoryReference})");
        }

        toReturn.Address = $"0x{arguments.Offset:X4}";

        if (arguments.Offset <= data.Length)
        {
            toReturn.Data = Convert.ToBase64String(data.Slice(arguments.Offset ?? 0, arguments.Count));
        }

        return toReturn;
    }

    #endregion

    #region DebugInfo

    protected override StackTraceResponse HandleStackTraceRequest(StackTraceArguments arguments)
    {
        var toReturn = new StackTraceResponse();
        var frame = new StackFrame();

        frame.Id = 1;
        frame.Name = "Main";

        // todo: handle ram \ rom banks
        var pc = _emulator.Pc;
        var id = SourceMap.GetUniqueAddress(pc, 0);
        if (MemoryToSourceMap.TryGetValue(id, out var instruction))
        {
            frame.Line = instruction.Line.Source.LineNumber;
            frame.Source = new Source()
            {
                Name = Path.GetFileName(instruction.Line.Source.Name),
                Path = instruction.Line.Source.Name,
            };
        }

        toReturn.StackFrames.Add(frame);

        return toReturn;
    }

    protected override ScopesResponse HandleScopesRequest(ScopesArguments arguments)
    {
        var toReturn = new ScopesResponse();

        toReturn.Scopes.AddRange(_scopeManager.AllScopes);

        return toReturn;
    }

    protected override VariablesResponse HandleVariablesRequest(VariablesArguments arguments)
    {
        var toReturn = new VariablesResponse();

        var scope = _scopeManager.GetScope(arguments.VariablesReference);

        if (scope != null)
        {
            toReturn.Variables.AddRange(scope.Variables.Select(i => i.GetVariable()));

            return toReturn;
        }
        var variable = _variableManager.Get(arguments.VariablesReference);

        if (variable == null)
            return toReturn;

        var children = variable as VariableChildren;
        if (children != null)
        {
            toReturn.Variables.AddRange(children.Children.Select(i => i.GetVariable()));

            return toReturn;
        }

        var index = variable as VariableIndex;
        if (index != null)
        {
            toReturn.Variables.AddRange(index.GetChildren());

            return toReturn;

        }

        return null;
    }

    #endregion

    #region SourceMap

    // Construct the source map for the debugger.
    public void ConstructSourceMap(CompileResult result)
    {
        var state = result.State;

        foreach (var segment in state.Segments.Values)
        {
            foreach (var defaultProc in segment.DefaultProcedure.Values)
            {
                MapProc(defaultProc);
            }
        }
    }

    private void MapProc(Procedure proc)
    {
        foreach (var line in proc.Data)
        {
            var toAdd = new SourceMap(line, 0);

            if (MemoryToSourceMap.ContainsKey(toAdd.UniqueAddress))
                throw new Exception("Could add line, as it was already in the hashset.");

            MemoryToSourceMap.Add(toAdd.UniqueAddress, toAdd);

            HashSet<CodeMap> lineMap;
            if (!SourceToMemoryMap.ContainsKey(line.Source.Name))
            {
                lineMap = new HashSet<CodeMap>();
                SourceToMemoryMap.Add(line.Source.Name, lineMap);
            }
            else
            {
                lineMap = SourceToMemoryMap[line.Source.Name];
            }

            lineMap.Add(new CodeMap(line.Source.LineNumber, line.Address, 0, line));
        }

        foreach (var p in proc.Procedures)
        {
            MapProc(p);
        }
    }

    #endregion
}

public class CodeMap
{
    public int LineNumber { get; }
    public int Address { get; }
    public int Bank { get; }
    public IOutputData Line { get; }

    public CodeMap(int lineNumber, int address, int bank, IOutputData line)
    {
        LineNumber = lineNumber;
        Address = address;
        Bank = bank;
        Line = line;
    }

    public override int GetHashCode() => LineNumber;

    public override bool Equals(object? obj)
    {
        if (obj == null) return false;

        var o = obj as CodeMap;

        if (o == null) return false;

        return LineNumber == o.LineNumber;
    }
}

public class SourceMap
{
    public static int GetUniqueAddress(int address, int bank) => address + bank * 0x10000;

    public int Address { get; }
    public int Bank { get; }
    public IOutputData Line { get; }
    public int UniqueAddress => Address + Bank * 0x10000;
    public SourceMap(IOutputData line, int bank)
    {
        Line = line;
        Address = line.Address;
    }
}

public class IdManager
{
    private int _id = 1;
    public int GetId() => _id++;
}
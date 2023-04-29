using BitMagic.Common;
using BitMagic.Compiler;
using BitMagic.Compiler.Exceptions;
using BitMagic.Decompiler;
using BitMagic.Machines;
using BitMagic.X16Emulator;
using BitMagic.X16Emulator.Display;
using BitMagic.X16Emulator.Snapshot;
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
    private BreakpointManager _breakpointManager;
    private readonly ScopeManager _scopeManager;
    private readonly SourceMapManager _sourceMapManager;
    private VariableManager _variableManager;
    private StackManager _stackManager;
    private SpriteManager _spriteManager;
    private PaletteManager _paletteManager;
    private DisassemblerManager _disassemblerManager;
    private ExpressionManager _expressionManager;

    private readonly IdManager _idManager;

    //public Dictionary<int, SourceMap> MemoryToSourceMap { get; } = new();
    //public Dictionary<string, HashSet<CodeMap>> SourceToMemoryMap { get; } = new();

    private Dictionary<int, CodeMap> _GotoTargets = new();

    private bool _running = true;

    private ManualResetEvent _runEvent = new ManualResetEvent(false);
    private object SyncObject = new object();

    private X16DebugProject? _debugProject;
    private IMachine? _machine;
    private readonly string _defaultRomFile;

    private readonly IEmulatorLogger _logger;

    // This will be started on a second thread, seperate to the emulator
    public X16Debug(Func<Emulator> getNewEmulatorInstance, Stream stdIn, Stream stdOut, string romFile, IEmulatorLogger? logger = null)
    {
        _logger = logger ?? new DebugLogger(this);

        _defaultRomFile = romFile;
        _getNewEmulatorInstance = getNewEmulatorInstance;
        _emulator = getNewEmulatorInstance();

        _idManager = new IdManager();

        _sourceMapManager = new SourceMapManager(_idManager);
        _scopeManager = new ScopeManager(_idManager);

        _disassemblerManager = new DisassemblerManager(_sourceMapManager, _emulator, _idManager);
        _breakpointManager = new BreakpointManager(_emulator, this, _sourceMapManager, _idManager, _disassemblerManager);
        _stackManager = new StackManager(_emulator, _idManager, _sourceMapManager, _disassemblerManager);
        _spriteManager = new SpriteManager(_emulator);
        _paletteManager = new PaletteManager(_emulator);
        _variableManager = new VariableManager(_idManager, _emulator, _scopeManager, _paletteManager, _spriteManager, _stackManager);
        _expressionManager = new ExpressionManager(_variableManager);

        InitializeProtocolClient(stdIn, stdOut);

        if (_debugProject?.ShowDAPMessages ?? false)
        {
            Protocol.RequestReceived += Protocol_RequestReceived;
            Protocol.RequestCompleted += Protocol_RequestCompleted;
            Protocol.LogMessage += Protocol_LogMessage;
        }
    }

    private void Protocol_LogMessage(object? sender, LogEventArgs e)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("LOG ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(JsonConvert.SerializeObject(e));
        Console.ResetColor();
    }

    public IEmulatorLogger Logger => _logger;

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

    public SysThread? DebugThread => _debugThread;

    /// <summary>
    /// Runs the adaptor, and returns once debugging is over.
    /// </summary>
    public void Run()
    {
        this.Protocol.Run();
        this.Protocol.WaitForReader();
    }

    #region Initialize/Disconnect

    protected override InitializeResponse HandleInitializeRequest(InitializeArguments arguments)
    {
        this.Protocol.SendEvent(new InitializedEvent());

        return new InitializeResponse()
        {
            SupportsReadMemoryRequest = true,
            SupportsDisassembleRequest = true,
            SupportsWriteMemoryRequest = true,
            SupportsInstructionBreakpoints = true,
            SupportsGotoTargetsRequest = true,
            SupportsLoadedSourcesRequest = true,
            //SupportsSetVariable = true,
            SupportsLogPoints = true,
            SupportsConditionalBreakpoints = true,
            SupportsHitConditionalBreakpoints = true,
        };
    }

    protected override LaunchResponse HandleLaunchRequest(LaunchArguments arguments)
    {
        var toCompile = arguments.ConfigurationProperties.GetValueAsString("program");

        if (!File.Exists(toCompile))
        {
            throw new ProtocolException($"Launch failed because '{toCompile}' does not exist.");
        }

        if (string.Equals(Path.GetExtension(toCompile), ".json", StringComparison.CurrentCultureIgnoreCase))
        {
            if (!File.Exists(toCompile))
                throw new ProtocolException($"File not found '{toCompile}'.");

            try
            {
                _debugProject = JsonConvert.DeserializeObject<X16DebugProject>(File.ReadAllText(toCompile));
                if (_debugProject == null)
                    throw new ProtocolException($"Could not deseralise {toCompile} into a X16DebugProject.");
            }
            catch (Exception e)
            {
                throw new ProtocolException(e.Message);
            }
        }
        else
        {
            _debugProject = new X16DebugProject();
            _debugProject.Source = toCompile;
        }

        // Load ROM
        var rom = _defaultRomFile;
        if (!string.IsNullOrWhiteSpace(_debugProject.RomFile))
        {
            if (File.Exists(_debugProject.RomFile))
                rom = _debugProject.RomFile;
            else
                _logger.LogError($"*** Project Rom file not found: {_debugProject.RomFile}");
        }

        if (!File.Exists(rom))
        {
            _logger.LogError($"*** Rom file not found: {rom}");
            throw new Exception($"Rom file not found {rom}");
        }

        _logger.Log($"Loading '{rom}'... ");
        var romData = File.ReadAllBytes(rom);
        for (var i = 0; i < romData.Length; i++)
        {
            _emulator.RomBank[i] = romData[i];
        }
        _logger.LogLine("Done.");
        // end load rom

        _disassemblerManager.SetProject(_debugProject);

        if (!File.Exists(_debugProject.Source))
        {
            var testSource = Path.Join(arguments.ConfigurationProperties.GetValueAsString("cwd"), _debugProject.Source);
            if (File.Exists(testSource))
                _debugProject.Source = testSource;
        }

        if (!string.IsNullOrWhiteSpace(_debugProject.Machine) && string.IsNullOrWhiteSpace(_debugProject.Source))
        {
            _machine = MachineFactory.GetMachine(_debugProject!.Machine) ?? throw new Exception($"Machine not returned for {_debugProject!.Machine}");

            if (_machine != null)
            {
                _sourceMapManager.AddSymbolsFromMachine(_machine);
            }
        }

        foreach (var symbols in _debugProject!.Symbols)
        {
            try
            {
                _logger.Log($"Loading Symbols {symbols.Name}... ");
                var bankData = _emulator.RomBank.Slice((symbols.RomBank ?? 0) * 0x4000, 0x4000).ToArray();
                _sourceMapManager.LoadSymbols(symbols);
                _sourceMapManager.LoadJumpTable(symbols.RangeDefinitions, 0xc000, symbols.RomBank ?? 0, bankData);

                _logger.Log($"Decompiling... ");

                _disassemblerManager.DecompileRomBank(bankData, symbols.RomBank ?? 0);

                _logger.LogLine("Done.");
            }
            catch (Exception e)
            {
                throw new ProtocolException(e.Message);
            }
        }

        // disassemble rom banks if the symbols weren't set
        for (var i = 0; i < 10; i++)
        {
            if (_disassemblerManager.IsRomDecompiled(i))
                continue;

            var bankData = _emulator.RomBank.Slice(i * 0x4000, 0x4000).ToArray();

            _logger.Log($"Decompiling Rom Bank {i}... ");

            _disassemblerManager.DecompileRomBank(bankData, i);

            _logger.LogLine("Done.");
        }

        // Keyboard buffer
        if (_debugProject.KeyboardBuffer != null && _debugProject.KeyboardBuffer.Any())
        {
            _emulator.Smc.SmcKeyboard_ReadNoData = 0;
            foreach (var i in _debugProject.KeyboardBuffer.Take(16))
                _emulator.SmcBuffer.PushKeyboard(i);
        }

        // Mouse buffer
        if (_debugProject.MouseBuffer != null && _debugProject.MouseBuffer.Any())
        {
            _emulator.Smc.SmcKeyboard_ReadNoData = 0;
            foreach (var i in _debugProject.MouseBuffer.Take(8))
                _emulator.SmcBuffer.PushMouseByte(i);
        }

        // RTC NVRAM
        if (_debugProject.NvRam != null)
        {
            byte[]? nvramData = null;
            if (_debugProject.NvRam.Data.Any())
            {
                nvramData = _debugProject.NvRam.Data;
            }
            else if (!string.IsNullOrWhiteSpace(_debugProject.NvRam.File))
            {
                if (File.Exists(_debugProject.NvRam.File))
                {
                    _logger.LogLine($"Loading NVRAM from '{_debugProject.NvRam.File}'.");
                    try
                    {
                        nvramData = File.ReadAllBytes(_debugProject.NvRam.File).Take(0x40).ToArray();
                    }
                    catch (Exception e)
                    {
                        _logger.LogError($"Could not read NVRAM File'.");
                        _logger.LogError(e.Message);
                    }
                }
                else
                {
                    _logger.LogError($"NVRAM File not found '{_debugProject.NvRam.File}'.");
                }
            }

            if (nvramData != null)
            {
                int i = 0;
                while (i < 0x40 && i < nvramData.Length)
                {
                    _emulator.RtcNvram[i] = nvramData[i];
                    i++;
                }
            }
        }

        try
        {
            var project = new Project();
            if (!string.IsNullOrWhiteSpace(_debugProject.Source))
            {
                _logger.Log($"Compiling {_debugProject.Source}");
                project.Code.Load(_debugProject.Source).GetAwaiter().GetResult();

                var compiler = new Compiler.Compiler(project);

                var compileResult = compiler.Compile().GetAwaiter().GetResult();

                _sourceMapManager.ConstructSourceMap(compileResult);

                if (compileResult.Warnings.Any())
                {
                    _logger.LogLine(" Warnings:");
                    foreach (var warning in compileResult.Warnings)
                    {
                        _logger.LogLine(warning);
                    }
                }

                var prg = compileResult.Data["Main"].ToArray();

                if (_debugProject.RunSource)
                {
                    var destAddress = 0x801;
                    for (var i = 2; i < prg.Length; i++)
                    {
                        _emulator.Memory[destAddress++] = prg[i];
                    }
                    _emulator.Pc = 0x801;
                    _logger.LogLine($" Done. Injecting {prg.Length:#,##0} bytes. Starting at 0x801");
                }
                else
                {
                    var filename = Path.GetFileName(_debugProject.Source);
                    if (_emulator.SdCard == null)
                        throw new Exception("SDCard is null");

                    filename = Path.GetFileNameWithoutExtension(filename) + ".prg";
                    _emulator.SdCard.AddCompiledFile(filename, prg);
                    _logger.LogLine($" Done. Created '{filename}' ({prg.Length:#,##0} bytes.)");
                    _emulator.Pc = (ushort)((_emulator.RomBank[0x3ffd] << 8) + _emulator.RomBank[0x3ffc]);
                }

                if (!string.IsNullOrWhiteSpace(_debugProject.SourcePrg))
                {
                    _logger.Log($"Writing to local file '{_debugProject.SourcePrg}'... ");
                    File.WriteAllBytes(_debugProject.SourcePrg, prg);
                    _logger.LogLine("Done.");
                }
            }
            else
            {
                _emulator.Pc = (ushort)((_emulator.RomBank[0x3ffd] << 8) + _emulator.RomBank[0x3ffc]);
            }

        }
        catch (Exception e)
        {
            throw new ProtocolException(e.Message);
        }

        _emulator.Stepping = true; // arguments.ConfigurationProperties.Contains()
        _emulator.Control = Control.Paused; // wait for main window
        _emulator.FrameControl = FrameControl.Synced;

        _running = true;
        _debugThread = new SysThread(DebugLoop);
        _debugThread.Name = "DebugLoop Thread";
        _debugThread.Priority = ThreadPriority.Highest;
        _debugThread.Start();

        _windowThread = new SysThread(() => EmulatorWindow.Run(_emulator));
        _windowThread.Name = "Debugger Window";
        _windowThread.Start();

        return new LaunchResponse();
    }

    protected override DisconnectResponse HandleDisconnectRequest(DisconnectArguments arguments)
    {
        _idManager.Clear();
        _sourceMapManager.Clear();
        EmulatorWindow.Stop();

        // persist anything that needs it
        // RTC NVRAM
        if (_debugProject != null && _debugProject.NvRam != null && !string.IsNullOrWhiteSpace(_debugProject.NvRam.WriteFile))
        {
            try
            {
                _logger.LogLine($"Saving NVRAM to {_debugProject.NvRam.WriteFile}");
                File.WriteAllBytes(_debugProject.NvRam.WriteFile, _emulator.RtcNvram.ToArray());
            }
            catch (Exception e)
            {
                _logger.LogError("Error Saving NVRAM:");
                _logger.LogError(e.Message);
            }
        }

        _emulator = _getNewEmulatorInstance();
        _disassemblerManager = new DisassemblerManager(_sourceMapManager, _emulator, _idManager);
        _breakpointManager = new BreakpointManager(_emulator, this, _sourceMapManager, _idManager, _disassemblerManager);
        _stackManager = new StackManager(_emulator, _idManager, _sourceMapManager, _disassemblerManager);
        _spriteManager = new SpriteManager(_emulator);
        _paletteManager = new PaletteManager(_emulator);
        _expressionManager = new ExpressionManager(_variableManager);
        _variableManager = new VariableManager(_idManager, _emulator, _scopeManager, _paletteManager, _spriteManager, _stackManager);

        if (_machine != null)
        {
            _sourceMapManager.AddSymbolsFromMachine(_machine);
        }

        return new DisconnectResponse();
    }

    #endregion

    #region Breakpoints

    protected override SetBreakpointsResponse HandleSetBreakpointsRequest(SetBreakpointsArguments arguments)
        => _breakpointManager.HandleSetBreakpointsRequest(arguments);

    protected override SetExceptionBreakpointsResponse HandleSetExceptionBreakpointsRequest(SetExceptionBreakpointsArguments arguments)
    {
        return new SetExceptionBreakpointsResponse();
    }

    protected override SetInstructionBreakpointsResponse HandleSetInstructionBreakpointsRequest(SetInstructionBreakpointsArguments arguments)
    {
        var toReturn = new SetInstructionBreakpointsResponse();



        return toReturn;
    }

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
        _emulator.Stepping = true;
        lock (SyncObject)
        {
            _runEvent.Set();
        }

        return new StepInResponse();
    }

    protected override StepOutResponse HandleStepOutRequest(StepOutArguments arguments)
    {
        _emulator.Stepping = false;
        _stackManager.SetBreakpointOnCaller();

        lock (SyncObject)
        {
            _runEvent.Set();
        }

        return new StepOutResponse();
    }

    protected override NextResponse HandleNextRequest(NextArguments arguments)
    {
        if (_emulator.Memory[_emulator.Pc] == 0x20)
        {
            _emulator.StackBreakpoints[_emulator.StackPointer - 0x100] = 0x01;

            _emulator.Stepping = false;
            lock (SyncObject)
            {
                _runEvent.Set();
            }
        }
        else // if its not a jsr, just step
        {
            _emulator.Stepping = true;
            lock (SyncObject)
            {
                _runEvent.Set();
            }
        }

        return new NextResponse();
    }

    protected override GotoResponse HandleGotoRequest(GotoArguments arguments)
    {
        var toReturn = new GotoResponse();

        if (!_GotoTargets.ContainsKey(arguments.TargetId))
            return toReturn;

        var destination = _GotoTargets[arguments.TargetId];

        // todo: set banking!!
        _emulator.Pc = (ushort)destination.Address;
        _emulator.Stepping = true;

        _GotoTargets.Clear();

        // we dont do anything
        this.Protocol.SendEvent(new StoppedEvent(StoppedEvent.ReasonValue.Step, "Stepping", 0, null, true));

        return toReturn;
    }

    protected override GotoTargetsResponse HandleGotoTargetsRequest(GotoTargetsArguments arguments)
    {
        var toReturn = new GotoTargetsResponse();

        var file = _sourceMapManager.GetSourceFileMap(arguments.Source.Path);
        if (file == null)
            return toReturn;

        var line = file.FirstOrDefault(i => i.LineNumber == arguments.Line);

        if (line == null)
            return toReturn;

        var id = _idManager.GetId();

        _GotoTargets.Add(id, line);

        toReturn.Targets.Add(new GotoTarget() { InstructionPointerReference = $"0x{line.Address}", Id = id, Line = line.LineNumber, Label = $"0x{line.Address}" });
        return toReturn;
    }

    protected override PauseResponse HandlePauseRequest(PauseArguments arguments)
    {
        var toReturn = new PauseResponse();

        _emulator.Stepping = true;

        return toReturn;
    }

    #endregion

    #region Debug Loop

    // Run the emulator, handle stops, breakpoints, etc.
    // This is running in _debugThread.
    private void DebugLoop()
    {
        _logger.LogLine("Starting emulator");

        // load in SD Card files here.
        foreach (var filename in _debugProject!.SdCardFiles.Where(i => !string.IsNullOrWhiteSpace(i)))
        {
            if (_emulator.SdCard == null) throw new Exception("SDCard is null!");

            if (File.Exists(filename))
            {
                _emulator.SdCard.AddFiles(filename);
                continue;
            }

            if (Directory.Exists(filename))
            {
                _emulator.SdCard.AddDirectory(filename);
                continue;
            }

            var wildcard = Path.GetFileName(filename);
            var path = Path.GetDirectoryName(filename);
            if (!Directory.Exists(path))
            {
                _logger.LogError($"Cannot find directory: {path}");
                continue;
            }

            foreach (var actFilename in Directory.GetFiles(path, wildcard))
            {
                _emulator.SdCard.AddFiles(actFilename);
            }
        }

        Snapshot? snapshot = _debugProject.CaptureChanges ? _emulator.Snapshot() : null;
        while (_running)
        {
            var returnCode = _emulator.Emulate();

            if (_debugProject.CaptureChanges)
            {
                var changes = snapshot!.Compare();

                if (changes != null)
                    _variableManager.SetChanges(changes);
            }

            // invalidate any decompiled source
            if (_emulator.Stepping)
            {
                foreach (var i in _idManager.GetObjects<DecompileReturn>(ObjectType.DecompiledData))
                {
                    if (i.Volatile)
                        Protocol.SendEvent(new LoadedSourceEvent()
                        {
                            Reason = LoadedSourceEvent.ReasonValue.Changed,
                            Source = new Source()
                            {
                                Name = i.Name,
                                Path = i.Path,
                                Origin = i.Origin,
                                SourceReference = i.ReferenceId
                            }
                        });
                }
            }

            bool wait = true;
            switch (returnCode)
            {
                case Emulator.EmulatorResult.Stepping:
                    this.Protocol.SendEvent(new StoppedEvent(StoppedEvent.ReasonValue.Step, "Stepping", 0, null, true));
                    _emulator.Stepping = true;
                    break;
                case Emulator.EmulatorResult.DebugOpCode:
                    this.Protocol.SendEvent(new StoppedEvent(StoppedEvent.ReasonValue.Breakpoint, "STP hit", 0, null, true));
                    _emulator.Stepping = true;
                    break;
                case Emulator.EmulatorResult.Breakpoint:
                    var (breakpoint, hitCount)= _breakpointManager.GetCurrentBreakpoint(_emulator.Pc, _emulator.Memory[0x00], _emulator.Memory[0x01]);

                    if (breakpoint != null)
                    {
                        var condition = true;
                        if (!string.IsNullOrWhiteSpace(breakpoint.Condition))
                        {
                            condition = _expressionManager.ConditionMet(breakpoint.Condition);
                        }

                        if (condition && !string.IsNullOrWhiteSpace(breakpoint.HitCondition))
                        {
                            condition = _expressionManager.ConditionMet($"{hitCount} {breakpoint.HitCondition}");
                        }

                        if (!string.IsNullOrEmpty(breakpoint.LogMessage))
                        {
                            if (condition)
                            {
                                var message = _expressionManager.FormatMessage(breakpoint.LogMessage);
                                _logger.LogLine(message);
                            }
                            _emulator.Stepping = false;
                            wait = false;
                            break;
                        } 
                        else if (!condition)
                        {
                            _emulator.Stepping = false;
                            wait = false;
                            break;
                        }
                    }

                    this.Protocol.SendEvent(new StoppedEvent(StoppedEvent.ReasonValue.Breakpoint, "Breakpoint hit", 0, null, true));

                    _emulator.Stepping = true;
                    break;
                default:
                    _logger.LogLine($"Stopping. Result : {returnCode}");
                    this.Protocol.SendEvent(new ExitedEvent((int)returnCode));
                    this.Protocol.SendEvent(new TerminatedEvent());
                    _running = false;
                    return;
            }

            if (wait)
            {
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
                    Name = "CX16",
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
        var requestEnd = arguments.Count + arguments.Offset ?? 0;
        var unreadCount = 0;
        if (requestEnd > data.Length)
        {
            requestEnd = data.Length;
            unreadCount = arguments.Count - requestEnd;
        }

        toReturn.UnreadableBytes = unreadCount;

        if (arguments.Offset <= data.Length)
        {
            toReturn.Data = Convert.ToBase64String(data.Slice(arguments.Offset ?? 0, requestEnd - arguments.Offset ?? 0));
        }

        return toReturn;
    }

    protected override WriteMemoryResponse HandleWriteMemoryRequest(WriteMemoryArguments arguments)
    {
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

        var toWrite = Convert.FromBase64String(arguments.Data);
        var idx = arguments.Offset ?? 0;
        for (var i = 0; i < toWrite.Length; i++)
            data[idx++] = toWrite[i];

        var toReturn = new WriteMemoryResponse();

        toReturn.BytesWritten = toWrite.Length;
        toReturn.Offset = arguments.Offset;

        return toReturn;
    }

    #endregion

    #region DebugInfo

    protected override StackTraceResponse HandleStackTraceRequest(StackTraceArguments arguments)
    {
        var toReturn = new StackTraceResponse();

        _stackManager.GenerateCallStack();
        toReturn.StackFrames.AddRange(_stackManager.GetCallStack);
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
            toReturn.Variables.AddRange(index.GetChildren().Skip(arguments.Start ?? 0).Take(arguments.Count ?? int.MaxValue));

            return toReturn;
        }

        return toReturn;
    }

    protected override SetVariableResponse HandleSetVariableRequest(SetVariableArguments arguments)
    {
        return new SetVariableResponse();
    }

    protected override EvaluateResponse HandleEvaluateRequest(EvaluateArguments arguments)
    {
        return _expressionManager.Evaluate(arguments);
    }

    #endregion

    #region Loaded Source

    protected override LoadedSourcesResponse HandleLoadedSourcesRequest(LoadedSourcesArguments arguments)
    {
        var toReturn = new LoadedSourcesResponse();

        foreach (var i in _idManager.GetObjects<DecompileReturn>(ObjectType.DecompiledData))
        {
            toReturn.Sources.Add(new Source
            {
                Name = i.Name,
                Path = i.Path,
                SourceReference = i.ReferenceId,
                Origin = i.Origin,
            });
        }

        return toReturn;
    }

    protected override SourceResponse HandleSourceRequest(SourceArguments arguments)
    {
        var toReturn = new SourceResponse();

        var data = _idManager.GetObject<DecompileReturn>(arguments.SourceReference);

        if (data == null)
            return toReturn;

        var sb = new StringBuilder();

        if (data.Volatile)
        {
            data.Generate();
        }

        foreach (var i in data.Items.Values)
        {
            sb.AppendLine(i.Instruction);
        }

        toReturn.Content = sb.ToString();
        return toReturn;
    }

    #endregion

    #region Disassemble

    protected override DisassembleResponse HandleDisassembleRequest(DisassembleArguments arguments)
        => _disassemblerManager.HandleDisassembleRequest(arguments);

    #endregion
}

//#define SHOWDAP

using BitMagic.TemplateEngine.Compiler;
using BitMagic.Common;
using BitMagic.Compiler.Exceptions;
using BitMagic.Decompiler;
using BitMagic.Machines;
using BitMagic.X16Debugger.CustomMessage;
using BitMagic.X16Emulator;
using BitMagic.X16Emulator.Display;
using BitMagic.X16Emulator.Snapshot;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Utilities;
using Newtonsoft.Json;
using SysThread = System.Threading.Thread;
using BitMagic.Compiler.Files;
using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.Common.Address;

namespace BitMagic.X16Debugger;

public class X16Debug : DebugAdapterBase
{
    private Emulator _emulator;

    private SysThread? _debugThread;
    private SysThread? _windowThread;

    private readonly ServiceManager _serviceManager;

    private readonly Dictionary<int, CodeMap> _GotoTargets = new();

    private bool _running = true;

    private readonly ManualResetEvent _runEvent = new ManualResetEvent(false);
    private readonly object SyncObject = new object();

    private X16DebugProject? _debugProject;
    private IMachine? _machine;
    private readonly string _defaultRomFile;
    public IEmulatorLogger Logger { get; }

    private const int KERNEL_SetNam = 0xffbd;
    private const int KERNEL_Load = 0xffd5;
    private const int KERNEL_SetLfs = 0xffba;

    private string _setnam_value = "";
    private int _setlfs_secondaryaddress = 0;
    private int _setnam_fileaddress = 0;
    private bool _setnam_fileexists = false;

    private readonly string _officialEmulatorLocation;

    // This will be started on a second thread, seperate to the emulator
    public X16Debug(Func<Emulator> getNewEmulatorInstance, Stream stdIn, Stream stdOut, string romFile, string officialEmulatorLocation, IEmulatorLogger? logger = null)
    {
        Logger = logger ?? new DebugLogger(this);
        _serviceManager = new ServiceManager(getNewEmulatorInstance, this);
        _emulator = _serviceManager.Emulator;
        _officialEmulatorLocation = officialEmulatorLocation;
        _defaultRomFile = romFile;

        InitializeProtocolClient(stdIn, stdOut);

#if SHOWDAP
        Protocol.RequestReceived += Protocol_RequestReceived;
        Protocol.RequestCompleted += Protocol_RequestCompleted;
        Protocol.LogMessage += Protocol_LogMessage;
#endif

        Protocol.RegisterRequestType<PaletteRequest, PaletteRequestArguments, PaletteRequestResponse>(delegate (IRequestResponder<PaletteRequestArguments, PaletteRequestResponse> r)
        {
            HandlePaletteRequestAsync(r);
        });
    }

#if SHOWDAP
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
#endif

    public SysThread? DebugThread => _debugThread;

    /// <summary>
    /// Runs the adaptor, and returns once debugging is over.
    /// </summary>
    public void Run()
    {
        Protocol.Run();
        Protocol.WaitForReader();
    }

    #region Initialize/Disconnect

    protected override InitializeResponse HandleInitializeRequest(InitializeArguments arguments)
    {
        Protocol.SendEvent(new InitializedEvent());

        return new InitializeResponse()
        {
            SupportsReadMemoryRequest = true,
            SupportsDisassembleRequest = true,
            SupportsWriteMemoryRequest = true,
            //SupportsInstructionBreakpoints = true,
            SupportsGotoTargetsRequest = true,
            SupportsLoadedSourcesRequest = true,
            //SupportsSetVariable = true,
            SupportsLogPoints = true,
            SupportsConditionalBreakpoints = true,
            SupportsHitConditionalBreakpoints = true,
            //SupportsDataBreakpoints = true,
            SupportsExceptionInfoRequest = true,
            SupportsExceptionOptions = true,
            ExceptionBreakpointFilters = ExceptionManager.GetExceptionList()
        };
    }

    protected override LaunchResponse HandleLaunchRequest(LaunchArguments arguments)
    {
        var toCompile = arguments.ConfigurationProperties.GetValueAsString("program");
        var workspaceFolder = arguments.ConfigurationProperties.GetValueAsString("cwd");
        var stopOnEntry = false; // arguments.ConfigurationProperties.GetValueAsBool("stopOnEntry") ?? false;

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

        if (string.IsNullOrWhiteSpace(_debugProject.BasePath))
        {
            _debugProject.BasePath = workspaceFolder;
        }


        // Clear Ram
        if (_debugProject.MemoryFillValue != 0)
            _emulator.FillMemory(_debugProject.MemoryFillValue);

        // Load ROM
        var rom = _defaultRomFile;

        var emulatorExists = false;

        if (string.IsNullOrWhiteSpace(_debugProject.EmulatorDirectory))
        {
            _debugProject.EmulatorDirectory = _officialEmulatorLocation;
        }

        if (!string.IsNullOrWhiteSpace(_debugProject.EmulatorDirectory))
        {
            if (Directory.Exists(_debugProject.EmulatorDirectory))
            {
                Logger.LogLine($"Using emulator directory '{_debugProject.EmulatorDirectory}'.");
                emulatorExists = true;
            }
            else
                Logger.LogLine($"Emulator directory '{_debugProject.EmulatorDirectory}' does not exist.");
        }

        if (emulatorExists && File.Exists(Path.Combine(_debugProject.EmulatorDirectory, "rom.bin")))
        {
            rom = Path.Combine(_debugProject.EmulatorDirectory, "rom.bin");
        }

        if (!string.IsNullOrWhiteSpace(_debugProject.RomFile))
        {
            if (File.Exists(_debugProject.RomFile))
                rom = _debugProject.RomFile;
            else
                Logger.LogError($"*** Project Rom file not found: {_debugProject.RomFile}");
        }

        if (!File.Exists(rom))
        {
            Logger.LogError($"*** Rom file not found: {rom}");
            throw new Exception($"Rom file not found {rom}");
        }

        Logger.Log($"Loading Rom '{rom}'... ");
        var romData = File.ReadAllBytes(rom);
        for (var i = 0; i < romData.Length; i++)
        {
            _emulator.RomBank[i] = romData[i];
        }
        Logger.LogLine("Done.");
        // end load rom

        // Load Cartridge
        if (!string.IsNullOrWhiteSpace(_debugProject.Cartridge))
        {
            Logger.Log($"Loading Cartridge '{_debugProject.Cartridge}'... ");
            var result = _emulator.LoadCartridge(_debugProject.Cartridge);

            if (result.Result == CartridgeHelperExtension.LoadCartridgeResultCode.Ok)
            {
                Logger.LogLine("Done.");
                if (result.Size > 0)
                {
                    var current = _debugProject.RomBankNames.ToList();
                    while (current.Count < 32)
                        current.Add("");

                    for (var i = current.Count; i < 32 + Math.Round(result.Size / (double)0x4000, MidpointRounding.ToPositiveInfinity); i++)
                    {
                        current.Add($"Cartridge_{i:000}");
                    }
                    _debugProject.RomBankNames = current.ToArray();
                }
            }
            else
            {
                Logger.LogLine("Error.");
                Logger.LogError(result.Result switch
                {
                    CartridgeHelperExtension.LoadCartridgeResultCode.FileNotFound => "*** File not found.",
                    CartridgeHelperExtension.LoadCartridgeResultCode.FileTooBig => "*** File too big.",
                    _ => "*** Unknown error."
                });
            }
        }

        _serviceManager.DisassemblerManager.SetProject(_debugProject);

        if (!File.Exists(_debugProject.Source))
        {
            var testSource = Path.Join(_debugProject.BasePath, _debugProject.Source);
            if (File.Exists(testSource))
                _debugProject.Source = testSource;
        }

        if (!string.IsNullOrWhiteSpace(_debugProject.Machine) && string.IsNullOrWhiteSpace(_debugProject.Source))
        {
            _machine = MachineFactory.GetMachine(_debugProject!.Machine) ?? throw new Exception($"Machine not returned for {_debugProject!.Machine}");

            if (_machine != null)
            {
                _serviceManager.SourceMapManager.AddSymbolsFromMachine(_machine);
            }
        }

        if (emulatorExists)
        {
            var symbolsList = _debugProject.Symbols.ToList();
            for (var i = 0; i < _debugProject.RomBankNames.Length; i++)
            {
                var filename = Path.Combine(_debugProject.EmulatorDirectory, _debugProject.RomBankNames[i].ToLower() + ".sym");
                if (!File.Exists(filename))
                {
                    Logger.LogLine($"Default symbols file {filename} doesn't exist.");
                    continue;
                }

                var symbols = _debugProject.Symbols.FirstOrDefault(b => b.RomBank != null && b.RomBank == i);

                if (symbols != null && !string.IsNullOrWhiteSpace(symbols.Filename))
                    continue;

                if (symbols == null)
                {
                    symbols = new SymbolsFile() { RomBank = i };
                    symbolsList.Add(symbols);

                    if (i == 0)
                    {
                        symbols.RangeDefinitions = new[] { new RangeDefinition() { Type = "JumpTable", Start = "0xfebd", End = "0xff80" },
                                                           new RangeDefinition() { Start = "0xff81", End = "0xfff6" }};
                    }
                    else if (i == 2)
                    {
                        symbols.RangeDefinitions = new[] { new RangeDefinition() { Type = "JumpTable", Start = "0xc000", End = "0xc036" } };
                    }
                }

                symbols.Symbols = filename;
            }
            _debugProject.Symbols = symbolsList.ToArray();
        }

        foreach (var symbols in _debugProject!.Symbols)
        {
            try
            {
                Logger.Log($"Loading Symbols {symbols.Symbols}... ");
                var bankData = _emulator.RomBank.Slice((symbols.RomBank ?? 0) * 0x4000, 0x4000).ToArray();
                _serviceManager.SourceMapManager.LoadSymbols(symbols);
                _serviceManager.SourceMapManager.LoadJumpTable(symbols.RangeDefinitions, 0xc000, symbols.RomBank ?? 0, bankData);

                Logger.Log($"Decompiling... ");

                _serviceManager.DisassemblerManager.DecompileRomBank(bankData, symbols.RomBank ?? 0);

                Logger.LogLine("Done.");
            }
            catch (Exception e)
            {
                throw new ProtocolException(e.Message);
            }
        }

        // disassemble rom banks if the symbols weren't set
        for (var i = 0; i < _debugProject.RomBankNames.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(_debugProject.RomBankNames[i]))
                continue;

            if (_serviceManager.DisassemblerManager.IsRomDecompiled(i))
                continue;

            var bankData = _emulator.RomBank.Slice(i * 0x4000, 0x4000).ToArray();

            Logger.Log($"Decompiling Rom Bank {i}... ");

            _serviceManager.DisassemblerManager.DecompileRomBank(bankData, i);

            Logger.LogLine("Done.");
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
                    Logger.LogLine($"Loading NVRAM from '{_debugProject.NvRam.File}'.");
                    try
                    {
                        nvramData = File.ReadAllBytes(_debugProject.NvRam.File).Take(0x40).ToArray();
                    }
                    catch (Exception e)
                    {
                        Logger.LogError($"Could not read NVRAM File'.");
                        Logger.LogError(e.Message);
                    }
                }
                else
                {
                    Logger.LogError($"NVRAM File not found '{_debugProject.NvRam.File}'.");
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

        _serviceManager.BreakpointManager.DebuggerBreakpoints.Add(KERNEL_SetNam);
        _serviceManager.BreakpointManager.DebuggerBreakpoints.Add(KERNEL_Load);
        _serviceManager.BreakpointManager.DebuggerBreakpoints.Add(KERNEL_SetLfs);
        _serviceManager.BreakpointManager.SetDebuggerBreakpoints();

        if (!string.IsNullOrWhiteSpace(_debugProject.SdCard))
        {
            if (File.Exists(_debugProject.SdCard))
            {
                Logger.LogLine($"Loading SD Card '{_debugProject.SdCard}'...");
                var sdCard = new SdCard(_debugProject.SdCard, Logger);
                _emulator.LoadSdCard(sdCard);
            }
            else
            {
                Logger.LogLine($"Cannot find SD Card '{_debugProject.SdCard}'.");
            }
        }

        if (_emulator.SdCard == null)
        {
            var sdCard = new SdCard(16, Logger);
            _emulator.LoadSdCard(sdCard);
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(_debugProject.Source))
            {
                var (result, state) = _serviceManager.BitmagicBuilder.Build(_debugProject).GetAwaiter().GetResult();
                if (result != null)
                {
                    _serviceManager.ExpressionManager.SetState(state);

                    var prg = result.Source as IBinaryFile ?? throw new Exception("result is not a IBinaryFile!");

                    if (_debugProject.AutobootRun)
                    {
                        Logger.Log("Adding AUTOBOOT.X16... ");
                        if (_emulator.SdCard!.FileSystem.Exists("AUTOBOOT.X16"))
                        {
                            Logger.LogLine("Error. File already exists.");
                        }
                        else
                        {
                            _emulator.SdCard!.AddCompiledFile("AUTOBOOT.X16", AutobootCreator.GetAutoboot(prg.Name));
                            Logger.LogLine("Done.");
                        }
                    }

                    if (_debugProject.DirectRun && result != null)
                    {
                        result.Load(_emulator, 0x801, true, _serviceManager.SourceMapManager, _serviceManager.DebugableFileManager);

                        //prg.LoadDebuggerInfo(0x801, true, _serviceManager.SourceMapManager, _serviceManager.BreakpointManager);
                        _emulator.Pc = _debugProject.StartAddress != -1 ? (ushort)_debugProject.StartAddress : (ushort)0x801;
                        Logger.LogLine($"Injecting {prg!.Data.Count:#,##0} bytes. Starting at 0x801");
                    }
                    else
                    {
                        //var filename = Path.GetFileName(_debugProject.Source);
                        //if (_emulator.SdCard == null)
                        //    throw new Exception("SDCard is null");

                        //filename = Path.GetFileNameWithoutExtension(filename) + ".prg";
                        //_emulator.SdCard.AddCompiledFile(filename, prg.Data);
                        //Logger.LogLine($" Done. Created '{filename}' ({prg.Data.Length:#,##0} bytes.)");
                        _emulator.Pc = (ushort)((_emulator.RomBank[0x3ffd] << 8) + _emulator.RomBank[0x3ffc]);
                    }

                    _serviceManager.DebugableFileManager.AddBitMagicFilesToSdCard(_emulator.SdCard ?? throw new Exception("SDCard is null"));

                    if (!string.IsNullOrWhiteSpace(_debugProject.OutputFolder))
                    {
                        foreach (var f in _serviceManager.DebugableFileManager.GetBitMagicFiles())
                        {
                            var path = Path.Combine(_debugProject.OutputFolder, f.Filename);
                            Logger.Log($"Writing to '{path}'... ");
                            File.WriteAllBytes(path, f.Data.ToArray());
                            Logger.LogLine("Done.");
                        }
                    }
                }
                else
                {
                    Logger.LogLine("Build didn't result in a result.");
                }
            }
            else
            {
                _emulator.Pc = _debugProject.StartAddress != -1 ? (ushort)_debugProject.StartAddress : (ushort)((_emulator.RomBank[0x3ffd] << 8) + _emulator.RomBank[0x3ffc]);
            }

        }
        catch (CompilerLineException e)
        {
            var sourceFile = e.Line.Source.SourceFile;
            var lineNumber = e.Line.Source.LineNumber - 1;

            // its very possible the source hasn't been registered, so we need to do it.
            _serviceManager.DebugableFileManager.AddFiles(sourceFile);

            var wrapper = _serviceManager.DebugableFileManager.GetWrapper(sourceFile) ?? throw new Exception("Cannot find source file!");

            var ul = wrapper.FindUltimateSource(lineNumber, _serviceManager.DebugableFileManager);

            var path = sourceFile != null ? Path.GetRelativePath(workspaceFolder, sourceFile.Path) : "";

            Logger.LogError($"ERROR: \"{path ?? "??"}\" ({ul.lineNumber}) \"{e.Message}\"", ul.SourceFile, ul.lineNumber + 1);

            Protocol.SendEvent(new TerminatedEvent() { Restart = false });

            return new LaunchResponse();
        }
        catch (CompilerSourceException e)
        {
            var sourceFile = e.SourceFile.SourceFile;
            var lineNumber = e.SourceFile.LineNumber - 1;

            _serviceManager.DebugableFileManager.AddFiles(sourceFile);

            var wrapper = _serviceManager.DebugableFileManager.GetWrapper(sourceFile) ?? throw new Exception("Cannot find source file!");

            var ul = wrapper.FindUltimateSource(lineNumber, _serviceManager.DebugableFileManager);

            var path = sourceFile != null ? Path.GetRelativePath(workspaceFolder, sourceFile.Path) : "";

            Logger.LogError($"ERROR: \"{path ?? "??"}\" ({ul.lineNumber}) \"{e.Message}\"", ul.SourceFile, ul.lineNumber + 1);

            Protocol.SendEvent(new TerminatedEvent() { Restart = false });

            return new LaunchResponse();
        }
        catch (CompilerException e)
        {
            Logger.LogError($"ERROR: {e.Message}");

            Protocol.SendEvent(new TerminatedEvent() { Restart = false });

            return new LaunchResponse();
        }
        catch (TemplateCompilationException e)
        {
            Logger.LogLine(""); // ensure there is a new line
            foreach (var error in e.Errors)
            {
                var path = e.Filename != null ? Path.GetRelativePath(workspaceFolder, e.Filename) : "";
                var source = new BitMagicProjectFile(e.Filename);
                if (error.LineNumber >= 0)
                    Logger.LogError($"ERROR: \"{path ?? "??"}\" ({error.LineNumber}) \"{error.ErrorText}\"", source, error.LineNumber);
                else
                    Logger.LogLine($"ERROR: \"{path ?? "??"}\" \"{error.ErrorText}\"");
            }

            Protocol.SendEvent(new TerminatedEvent() { Restart = false });

            return new LaunchResponse();
        }
        catch (TemplateException e)
        {
            Logger.LogLine($"ERROR: {e.Message}");

            Protocol.SendEvent(new TerminatedEvent() { Restart = false });

            return new LaunchResponse();
        }
        catch (Exception e)
        {
            Logger.LogLine($"ERROR: {e.Message}");

            throw new ProtocolException(e.Message);
        }

        _emulator.Stepping = stopOnEntry;
        _emulator.Control = Control.Paused; // wait for main window
        _emulator.FrameControl = FrameControl.Synced;

        _running = true;
        _debugThread = new SysThread(DebugLoop);
        _debugThread.Name = "DebugLoop Thread";
        _debugThread.Priority = ThreadPriority.Highest;
        _debugThread.Start();

        _windowThread = new SysThread(() =>
        {
            try
            {
                EmulatorWindow.Run(_emulator);
            }
            catch (Exception e)
            {
                Logger.LogError(e.Message);
                throw new ProtocolException(e.Message);
            }
        });
        _windowThread.Name = "Debugger Window";
        _windowThread.Start();

        return new LaunchResponse();
    }

    protected override DisconnectResponse HandleDisconnectRequest(DisconnectArguments arguments)
    {
        EmulatorWindow.Stop();

        // persist anything that needs it
        // RTC NVRAM
        if (_debugProject != null && _debugProject.NvRam != null && !string.IsNullOrWhiteSpace(_debugProject.NvRam.WriteFile))
        {
            try
            {
                Logger.LogLine($"Saving NVRAM to {_debugProject.NvRam.WriteFile}");
                File.WriteAllBytes(_debugProject.NvRam.WriteFile, _emulator.RtcNvram.ToArray());
            }
            catch (Exception e)
            {
                Logger.LogError("Error Saving NVRAM:");
                Logger.LogError(e.Message);
            }
        }

        _emulator = _serviceManager.Reset();

        if (_machine != null)
        {
            _serviceManager.SourceMapManager.AddSymbolsFromMachine(_machine);
        }

        return new DisconnectResponse();
    }

    #endregion

    #region Breakpoints

    protected override SetBreakpointsResponse HandleSetBreakpointsRequest(SetBreakpointsArguments arguments)
        => _serviceManager.BreakpointManager.HandleSetBreakpointsRequest(arguments);

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
        _serviceManager.StackManager.SetBreakpointOnCaller();

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

        var file = _serviceManager.SourceMapManager.GetSourceFileMap(arguments.Source.Path);
        if (file == null)
            return toReturn;

        var line = file.FirstOrDefault(i => i.LineNumber == arguments.Line);

        if (line == null)
            return toReturn;

        var id = _serviceManager.IdManager.GetId();

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

    #region Exceptions

    protected override SetExceptionBreakpointsResponse HandleSetExceptionBreakpointsRequest(SetExceptionBreakpointsArguments arguments) =>
        _serviceManager.ExceptionManager.SetExceptionBreakpointsRequest(arguments);
    protected override ExceptionInfoResponse HandleExceptionInfoRequest(ExceptionInfoArguments arguments) =>
        _serviceManager.ExceptionManager.ExceptionInfoRequest(arguments);

    #endregion

    #region Debug Loop

    // Run the emulator, handle stops, breakpoints, etc.
    // This is running in _debugThread.
    private void DebugLoop()
    {
        Logger.LogLine("Starting emulator");

        // load in SD Card files here.
        foreach (var filename in _debugProject!.SdCardFiles.Where(i => !string.IsNullOrWhiteSpace(i)))
        {
            if (_emulator.SdCard == null) throw new Exception("SDCard is null!");

            var name = Path.GetFullPath(filename, _debugProject.BasePath);
            if (File.Exists(name))
            {
                _emulator.SdCard.AddFiles(name);
                continue;
            }

            if (Directory.Exists(name))
            {
                _emulator.SdCard.AddDirectory(name);
                continue;
            }

            var wildcard = Path.GetFileName(name);
            var path = Path.GetDirectoryName(name);
            if (!Directory.Exists(path))
            {
                Logger.LogError($"Cannot find directory: {path}");
                continue;
            }

            foreach (var actFilename in Directory.GetFiles(path, wildcard))
            {
                _emulator.SdCard.AddFiles(actFilename);
            }
        }

        foreach (var i in _serviceManager.IdManager.GetObjects<DecompileReturn>(ObjectType.DecompiledData))
        {
            Protocol.SendEvent(new LoadedSourceEvent()
            {
                Reason = LoadedSourceEvent.ReasonValue.Changed,
                Source = new Source()
                {
                    Name = i.Name,
                    Path = i.Path,
                    Origin = i.Origin.ToString(),
                    SourceReference = i.ReferenceId
                }
            });
        }

        Snapshot? snapshot = _emulator.Snapshot();
        while (_running)
        {
            var returnCode = _emulator.Emulate();

            var changes = snapshot!.Compare();

            if (changes != null)
                _serviceManager.VariableManager.SetChanges(changes);

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
                    var (breakpoint, hitCount, breakpointType) = _serviceManager.BreakpointManager.GetCurrentBreakpoint(_emulator.Pc, _emulator.Memory[0x00], _emulator.Memory[0x01]);

                    if ((breakpointType & 0x80) != 0)
                    {
                        // debugger breakpoint
                        HandleDebuggerBreakpoint(breakpointType);

                        if ((breakpointType & 0x7f) == 0) // this is just a debugger breakpoint, so continue
                        {
                            _emulator.Stepping = false;
                            wait = false;
                            break;
                        }
                    }

                    if (breakpoint != null)
                    {
                        var stepping = _emulator.Stepping;
                        var condition = true;
                        if (!string.IsNullOrWhiteSpace(breakpoint.Condition))
                        {
                            condition = _serviceManager.ExpressionManager.ConditionMet(breakpoint.Condition);
                        }

                        if (condition && !string.IsNullOrWhiteSpace(breakpoint.HitCondition))
                        {
                            condition = _serviceManager.ExpressionManager.ConditionMet($"{hitCount} {breakpoint.HitCondition}");
                        }

                        if (!string.IsNullOrEmpty(breakpoint.LogMessage))
                        {
                            if (condition)
                            {
                                var message = _serviceManager.ExpressionManager.FormatMessage(breakpoint.LogMessage);
                                if (!string.IsNullOrEmpty(message)) // only send actual messages, allows for simpler conditional logpoints
                                    Logger.LogLine(message);
                            }
                            _emulator.Stepping = stepping;
                            wait = false;
                            break;
                        }
                        else if (!condition)
                        {
                            _emulator.Stepping = stepping;
                            wait = false;
                            break;
                        }
                    }

                    this.Protocol.SendEvent(new StoppedEvent(StoppedEvent.ReasonValue.Breakpoint, "Breakpoint hit", 0, null, true));

                    _emulator.Stepping = true;
                    break;
                case Emulator.EmulatorResult.UnknownOpCode:
                    this.Protocol.SendEvent(new StoppedEvent(StoppedEvent.ReasonValue.Breakpoint, "Unknown OpCode hit", 0, null, true));
                    _emulator.Stepping = true;
                    break;
                case Emulator.EmulatorResult.BrkHit:
                    _serviceManager.ExceptionManager.LastException = "BRK";
                    this.Protocol.SendEvent(new StoppedEvent(StoppedEvent.ReasonValue.Exception, "BRK hit", 0, null, true));
                    _emulator.Stepping = true;

                    //Logger.LogLine($"Stopping. BRK hit at ${_emulator.Pc:X4} RAM Bank: ${_emulator.RamBankAct:X2} ROM Bank: ${_emulator.RomBankAct:X2}");
                    //this.Protocol.SendEvent(new ExitedEvent((int)returnCode));
                    //this.Protocol.SendEvent(new TerminatedEvent());
                    //_running = false;
                    break;
                default:
                    Logger.LogLine($"Stopping. Result : {returnCode}");
                    this.Protocol.SendEvent(new ExitedEvent((int)returnCode));
                    this.Protocol.SendEvent(new TerminatedEvent());
                    _running = false;
                    return;
            }

            if (wait)
            {
                EmulatorWindow.PauseAudio();

                if (changes != null)
                {
                    var toFlag = new HashSet<string>();

                    foreach (var i in changes.Changes)
                    {
                        if (i is MemoryChange memoryChange)
                        {
                            if (memoryChange.MemoryArea == MemoryAreas.Ram && memoryChange.Address < 0x9f00)
                            {
                                Protocol.SendEvent(new MemoryEvent() { MemoryReference = "main", Offset = memoryChange.Address, Count = 1 });
                                toFlag.Add("MainRam.bmasm");
                            }
                            else if (memoryChange.MemoryArea == MemoryAreas.Vram)
                            {
                                Protocol.SendEvent(new MemoryEvent() { MemoryReference = "vram", Offset = memoryChange.Address, Count = 1 });
                            }
                            else if (memoryChange.MemoryArea == MemoryAreas.BankedRam)
                            {
                                int bank = (memoryChange.Address & 0x1fe000) >> 13;
                                Protocol.SendEvent(new MemoryEvent() { MemoryReference = $"rambank_{bank}", Offset = memoryChange.Address - (bank * 0x2000), Count = 1 });
                                toFlag.Add($"z_Bank_0x{bank:X2}.bmasm");
                            }

                        }
                        else if (i is MemoryRangeChange memoryRangeChange)
                        {
                            if (memoryRangeChange.MemoryArea == MemoryAreas.Ram && memoryRangeChange.Start < 0x9f00)
                            {
                                Protocol.SendEvent(new MemoryEvent() { MemoryReference = "main", Offset = memoryRangeChange.Start, Count = memoryRangeChange.End - memoryRangeChange.Start });
                                toFlag.Add("MainRam.bmasm");
                            }
                            else if (memoryRangeChange.MemoryArea == MemoryAreas.Vram)
                            {
                                Protocol.SendEvent(new MemoryEvent() { MemoryReference = "vram", Offset = memoryRangeChange.Start, Count = memoryRangeChange.End - memoryRangeChange.Start });
                            }
                            else if (memoryRangeChange.MemoryArea == MemoryAreas.BankedRam)
                            {
                                int bank = (memoryRangeChange.Start & 0x1fe000) >> 13;
                                Protocol.SendEvent(new MemoryEvent() { MemoryReference = $"rambank_{bank}", Offset = memoryRangeChange.Start - (bank * 0x2000), Count = memoryRangeChange.End - memoryRangeChange.Start });
                                toFlag.Add($"z_Bank_0x{bank:X2}.bmasm");
                            }
                        }
                    }

                    if (toFlag.Any())
                    {
                        foreach (var i in _serviceManager.IdManager.GetObjects<DecompileReturn>(ObjectType.DecompiledData))
                        {
                            if (!toFlag.Contains(i.Name))
                                continue;

                            Protocol.SendEvent(new LoadedSourceEvent()
                            {
                                Reason = LoadedSourceEvent.ReasonValue.Changed,
                                Source = new Source()
                                {
                                    Name = i.Name,
                                    Path = i.Path,
                                    Origin = i.Origin.ToString(),
                                    SourceReference = i.ReferenceId
                                }
                            });
                        }
                    }
                }

                _runEvent.WaitOne(); // wait for a signal to continue
                lock (SyncObject)
                {
                    _runEvent.Reset();
                }

                _serviceManager.StackManager.Invalidate();
                EmulatorWindow.ContinueAudio();
            }
        }
    }

    private static readonly byte[] InvalidBytes = "\"*+,/:;<=>?[\\]|"u8.ToArray();
    private static bool Contains(byte[] array, byte val) => Array.IndexOf(array, val) >= 0;
    private void HandleDebuggerBreakpoint(int breakpointType)
    {
        if (_emulator.Pc == KERNEL_SetNam) // setnam
        {
            var filenameAddress = _emulator.X + (_emulator.Y << 8);
            var len = _emulator.A;

            var x = new MemoryWrapper(() => _emulator.Memory.ToArray());
            var filename = x[filenameAddress].FixedString(len);
            _setnam_value = filename;

            if (string.IsNullOrWhiteSpace(_setnam_value) || _setnam_value.All(i => i == 0))
            {
                // set first file.
                _setnam_value = _emulator.SdCard!.FileSystem.GetFiles("").FirstOrDefault() ?? "";
            }

            _setnam_fileaddress = 0;
            _setnam_fileexists = false;

            if (_setnam_value.Any(i => i < 0x20 || Contains(InvalidBytes, (byte)i)))
            {
                _setnam_fileexists = false;
            }
            else
            {
                if (_emulator.SdCard!.FileSystem.FileExists(_setnam_value))
                {
                    using var data = _emulator.SdCard.FileSystem.OpenFile(_setnam_value, FileMode.Open);

                    _setnam_fileaddress = data.ReadByte();
                    _setnam_fileaddress += data.ReadByte() << 8;

                    data.Close();
                    _setnam_fileexists = true;
                }
            }

            if (_setnam_fileexists)
                Logger.LogLine($"SETNAM called with '{filename}', found '{_setnam_value}' with header ${_setnam_fileaddress:X4}.");
            else
                Logger.LogLine($"SETNAM called with '{filename}', no file found.");

            return;
        }

        if (_emulator.Pc == KERNEL_Load) // load
        {
            if (!_setnam_fileexists)
            {
                Logger.LogLine($"LOAD called but file does not exist.");
                return;
            }

            if (_emulator.A != 0) // 1+ is verify, we dont care about that
            {
                Logger.LogLine($"LOAD called but in verify mode.");
                return;
            }

            var loadAddress = 0;
            if (_setlfs_secondaryaddress == 0 || _setlfs_secondaryaddress == 2)
            {
                loadAddress = _emulator.X + (_emulator.Y << 8);
                Logger.LogLine($"LOAD called with '{_setnam_value}' loading to ${loadAddress:X4} (parameters)");
            }
            else
            {
                loadAddress = _setnam_fileaddress;
                Logger.LogLine($"LOAD called with '{_setnam_value}' loading to ${loadAddress:X4} (file header)");
            }

            var debugableFilenew = _serviceManager.DebugableFileManager.GetFile_New(_setnam_value);
            if (debugableFilenew != null)
            {
                Logger.Log($"Loading debugger info for '{_setnam_value}'... ");

                var actualAddress = AddressFunctions.GetDebuggerAddress(loadAddress, _emulator);

                var breakpoints = debugableFilenew.FileLoaded(_emulator, actualAddress, _setlfs_secondaryaddress < 2, _serviceManager.SourceMapManager, _serviceManager.DebugableFileManager);
                Logger.LogLine("Done");

                foreach (var breakpoint in breakpoints)
                {
                    Protocol.SendEvent(new BreakpointEvent(BreakpointEvent.ReasonValue.Changed, breakpoint));
                }
            }
            else
            {
                var fileLength = (int)_emulator.SdCard!.FileSystem.GetFileLength(_setnam_value);
                    if (_setlfs_secondaryaddress < 2)
                        fileLength = -2;

                if (fileLength > 0)
                {
                    int toClear = fileLength;
                    if (loadAddress >= 0xa000 && loadAddress - 0xa000 + fileLength > 0x2000)
                    {
                        Logger.LogLine("Warning: LOAD called into Banked RAM, but the file is too large.");
                        toClear = 0x2000 - (loadAddress - 0xa000);
                    }

                    if (loadAddress >= 0xc000)
                    {
                        Logger.LogLine("Warning: LOAD called into ROM.");
                        toClear = 0;
                    }

                    if (loadAddress < 0xa000 && loadAddress + fileLength > 0x9f00)
                    {
                        Logger.LogLine("Warning: LOAD called into normal RAM, but will load past 0x9f00");
                        toClear = 0x9f00 - loadAddress;
                    }

                    if (toClear != 0)
                    {
                        Logger.LogLine($"Clearing breakpoints from ${loadAddress:X4} to ${loadAddress + fileLength:X4} (actual: ${toClear:X4})");
                        _serviceManager.BreakpointManager.ClearBreakpoints(loadAddress, toClear);
                    }
                }
            }

            //var debugableFile = _serviceManager.DebugableFileManager.GetFile(_setnam_value);
            //if (debugableFile != null)
            //{
            //    Logger.Log($"Loading debugger info for '{_setnam_value}'... ");

            //    var actualAddress = AddressFunctions.GetDebuggerAddress(loadAddress, _emulator);

            //    var breakpoints = debugableFile.LoadDebuggerInfo(actualAddress, _setlfs_secondaryaddress < 2, _serviceManager.SourceMapManager, _serviceManager.BreakpointManager);
            //    Logger.LogLine("Done");

            //    foreach (var breakpoint in breakpoints)
            //    {
            //        Protocol.SendEvent(new BreakpointEvent(BreakpointEvent.ReasonValue.Changed, breakpoint));
            //    }
            //}
            //else
            //{
            //    var fileLength = (int)_emulator.SdCard!.FileSystem.GetFileLength(_setnam_value);
            //    if (_setlfs_secondaryaddress < 2)
            //        fileLength = -2;

            //    Logger.LogLine($"Clearing breakpoints from ${loadAddress:X4} to {loadAddress + fileLength:X4}");
            //    _serviceManager.BreakpointManager.ClearBreakpoints(loadAddress, fileLength);
            //}

            return;
        }

        if (_emulator.Pc == KERNEL_SetLfs)
        {
            Logger.LogLine($"SETLFS A: {_emulator.A}, X: {_emulator.X}, Y: {_emulator.Y}");
            _setlfs_secondaryaddress = _emulator.Y;

            return;
        }
    }

    #endregion

    #region Inspection

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
        if (arguments.MemoryReference.StartsWith("rambank"))
        {
            int.TryParse(arguments.MemoryReference.Substring(8), out var bank);

            data = _emulator.RamBank.Slice(bank * 0x2000, 0x2000);
        }
        else if (arguments.MemoryReference.StartsWith("rombank"))
        {
            int.TryParse(arguments.MemoryReference.Substring(8), out var bank);

            data = _emulator.RomBank.Slice(bank * 0x4000, 0x4000);
        }
        else
        {
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

        _serviceManager.StackManager.GenerateCallStack();
        toReturn.StackFrames.AddRange(_serviceManager.StackManager.CallStack.Select(i => i.StackFrame));
        return toReturn;
    }

    protected override ScopesResponse HandleScopesRequest(ScopesArguments arguments)
    {
        var toReturn = new ScopesResponse();

        var current = _serviceManager.StackManager.CallStack.FirstOrDefault(i => i.StackFrame.Id == arguments.FrameId);

        _serviceManager.VariableManager.SetScope(current);

        var scopes = _serviceManager.ScopeManager.AllScopes;

        toReturn.Scopes.AddRange(scopes);

        return toReturn;
    }

    protected override VariablesResponse HandleVariablesRequest(VariablesArguments arguments)
    {
        var toReturn = new VariablesResponse();

        var scope = _serviceManager.ScopeManager.GetScope(arguments.VariablesReference);

        if (scope != null)
        {
            toReturn.Variables.AddRange(scope.Variables.Select(i => i.GetVariable()));

            return toReturn;
        }

        var variable = _serviceManager.VariableManager.Get(arguments.VariablesReference);

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
        return _serviceManager.ExpressionManager.Evaluate(arguments);
    }

    #endregion

    #region Loaded Source

    protected override LoadedSourcesResponse HandleLoadedSourcesRequest(LoadedSourcesArguments arguments)
    {
        var toReturn = new LoadedSourcesResponse();

        toReturn.Sources.AddRange(_serviceManager.IdManager.GetObjects<ISourceFile>(ObjectType.DecompiledData)
            .Where(i => !i.ActualFile)
            .Select(i => new Source
            {
                Name = i.Name,
                Path = i.Path,
                SourceReference = i.ReferenceId,
                Origin = i.Origin.ToString(),
            }));

        return toReturn;
    }

    protected override SourceResponse HandleSourceRequest(SourceArguments arguments)
    {
        var toReturn = new SourceResponse();

        var data = _serviceManager.IdManager.GetObject<ISourceFile>(arguments.SourceReference);

        if (data == null)
            return toReturn;

        data.UpdateContent(); // will only update if requird.

        toReturn.Content = string.Join(Environment.NewLine, data.Content);
        return toReturn;
    }

    #endregion

    #region Disassemble

    protected override DisassembleResponse HandleDisassembleRequest(DisassembleArguments arguments)
        => _serviceManager.DisassemblerManager.HandleDisassembleRequest(arguments);

    #endregion

    #region Custom X16 Messages

    protected override ResponseBody HandleProtocolRequest(string requestType, object requestArgs) =>
        requestType switch
        {
            "bm_palette" => HandlePaletteRequest(),
            _ => base.HandleProtocolRequest(requestType, requestArgs)
        };

    internal virtual void HandlePaletteRequestAsync(IRequestResponder<PaletteRequestArguments, PaletteRequestResponse> responder)
    {
        responder.SetResponse(HandlePaletteRequest());
    }

    //protected override DataBreakpointInfoResponse HandleDataBreakpointInfoRequest(DataBreakpointInfoArguments arguments)
    //{
    //    return new DataBreakpointInfoResponse();
    //}

    //protected override SetDataBreakpointsResponse HandleSetDataBreakpointsRequest(SetDataBreakpointsArguments arguments)
    //{
    //    return new SetDataBreakpointsResponse();
    //}

    private PaletteRequestResponse HandlePaletteRequest()
    {
        var toReturn = new PaletteRequestResponse() { DisplayPalette = _emulator.Palette.ToArray() };

        var vramPalette = _emulator.Vera.Vram.Slice(0x1fa00, 256 * 2);
        var palette = new List<VeraPaletteItem>();
        for (var i = 0; i < 256; i++)
        {
            palette.Add(new VeraPaletteItem()
            {
                R = (byte)(vramPalette[i * 2 + 1] & 0x0f),
                G = (byte)((vramPalette[i * 2] & 0xf0) >> 4),
                B = (byte)(vramPalette[i * 2] & 0x0f)
            });
        }
        toReturn.Palette = palette.ToArray();
        return toReturn;
    }

    #endregion
}

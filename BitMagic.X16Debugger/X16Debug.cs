using BitMagic.Common;
using BitMagic.Compiler;
using BitMagic.Compiler.Exceptions;
using BitMagic.Decompiler;
using BitMagic.Machines;
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
    private BreakpointManager _breakpointManager;
    private readonly ScopeManager _scopeManager;
    private readonly SourceMapManager _sourceMapManager;
    private readonly VariableManager _variableManager;
    private StackManager _stackManager;
    private SpriteManager _spriteManager;
    private PaletteManager _paletteManager;
    private DisassemblerManager _disassemblerManager;

    private readonly IdManager _idManager;

    //public Dictionary<int, SourceMap> MemoryToSourceMap { get; } = new();
    //public Dictionary<string, HashSet<CodeMap>> SourceToMemoryMap { get; } = new();

    private Dictionary<int, CodeMap> _GotoTargets = new();

    private bool _running = true;

    private ManualResetEvent _runEvent = new ManualResetEvent(false);
    private object SyncObject = new object();

    private X16DebugProject? _debugProject;
    private IMachine _machine;
    private readonly string _defaultRomFile;

    // This will be started on a second thread, seperate to the emulator
    public X16Debug(Func<Emulator> getNewEmulatorInstance, Stream stdIn, Stream stdOut, string romFile)
    {
        _defaultRomFile = romFile;
        _getNewEmulatorInstance = getNewEmulatorInstance;
        _emulator = getNewEmulatorInstance();

        _idManager = new IdManager();

        _sourceMapManager = new SourceMapManager(_idManager);
        _scopeManager = new ScopeManager(_idManager);
        _variableManager = new VariableManager(_idManager);

        _breakpointManager = new BreakpointManager(_emulator, this, _sourceMapManager, _idManager);

        _disassemblerManager = new DisassemblerManager(_sourceMapManager, _emulator, _idManager);
        _stackManager = new StackManager(_emulator, _idManager, _sourceMapManager, _disassemblerManager);
        _spriteManager = new SpriteManager(_emulator);
        _paletteManager = new PaletteManager(_emulator);

        SetupGlobalObjects();

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
                    new VariableMemory("RAM", "main", () => "CPU Visible Ram")
                })));

        scope = _scopeManager.GetScope("VERA", false);

        scope.AddVariable(
            _variableManager.Register(
                new VariableChildren("Data 0", "Byte", () => $"0x{_emulator.Memory[0x9F23]:X2}",
                new[] {
                    new VariableMap("Address", "DWord", () => $"0x{_emulator.Vera.Data0_Address:X5}"),
                    new VariableMap("Step", "Byte", () => $"{_emulator.Vera.Data0_Step}")
                }
            )));

        scope.AddVariable(
            _variableManager.Register(
                new VariableChildren("Data 1", "Byte", () => $"0x{_emulator.Memory[0x9F24]:X2}",
                new[] {
                    new VariableMap("Address", "DWord", () => $"0x{_emulator.Vera.Data1_Address:X5}"),
                    new VariableMap("Step", "Byte", () => $"{_emulator.Vera.Data1_Step}")
                }
            )));

        scope.AddVariable(
            _variableManager.Register(
                new VariableChildren("Layer 0", "String", () => _emulator.Vera.Layer0Enable ?
                    (_emulator.Vera.Layer0_BitMapMode ? $"{GetColourDepth(_emulator.Vera.Layer0_ColourDepth):0}bpp Bitmap" : $"{GetColourDepth(_emulator.Vera.Layer0_ColourDepth):0}bpp Tiles") :
                    "Disabled",
                new[] {
                    new VariableMap("Map Address", "DWord", () => $"0x{_emulator.Vera.Layer0_MapAddress:X5}"),
                    new VariableMap("Tile Address", "DWord", () => $"0x{_emulator.Vera.Layer0_TileAddress:X5}"),
                }
            )));

        scope.AddVariable(
            _variableManager.Register(
                new VariableChildren("Layer 1", "String", () => _emulator.Vera.Layer1Enable ?
                    (_emulator.Vera.Layer1_BitMapMode ? $"{GetColourDepth(_emulator.Vera.Layer1_ColourDepth):0}bpp Bitmap" : $"{GetColourDepth(_emulator.Vera.Layer1_ColourDepth):0}bpp Tiles") :
                    "Disabled",
                new[] {
                    new VariableMap("Map Address", "DWord", () => $"0x{_emulator.Vera.Layer1_MapAddress:X5}"),
                    new VariableMap("Tile Address", "DWord", () => $"0x{_emulator.Vera.Layer1_TileAddress:X5}"),
                }
            )));

        scope.AddVariable(
            _variableManager.Register(
                new VariableChildren("Sprites", "String", () => _emulator.Vera.SpriteEnable ?
                    "Enabled" :
                    "Disabled",
                new IVariableMap[] {
                    _variableManager.Register(
                            new VariableIndex("Sprites", _spriteManager.GetFunction)
                        )
                    ,
                    _variableManager.Register(
                        new VariableChildren("Renderer", "String", () => $"",
                        new[] {
                            new VariableMap("Render Mode", "uint", () => $"{_emulator.Vera.Sprite_Render_Mode}"),
                            new VariableMap("VRAM Wait", "uint", () => $"{_emulator.Vera.Sprite_Wait}"),
                            new VariableMap("Sprite Index", "uint", () => $"{_emulator.Vera.Sprite_Position}"),
                            new VariableMap("Snapped X", "uint", () => $"{_emulator.Vera.Sprite_X}"),
                            new VariableMap("Snapped Y", "uint", () => $"{_emulator.Vera.Sprite_Y}"),
                            new VariableMap("Snapped Width", "uint", () => $"{_emulator.Vera.Sprite_Width}"),
                            new VariableMap("Depth", "uint", () => $"{_emulator.Vera.Sprite_Depth}"),
                            new VariableMap("Colission mask", "uint", () => $"0b{Convert.ToString(_emulator.Vera.Sprite_CollisionMask, 2).PadLeft(4, '0')}"),
                            })
                        )
                    }
                )));

        _spriteManager.Register(_variableManager);

        scope.AddVariable(
                    _variableManager.Register(
                            new VariableIndex("Palette", _paletteManager.GetFunction)
                        )
                );

        scope.AddVariable(new VariableMemory("VRAM", "vram", () => "0x20000 bytes"));

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

        scope = _scopeManager.GetScope("Display", false);

        scope.AddVariable(new VariableMap("Beam X", "Word", () => $"{_emulator.Vera.Beam_X}"));
        scope.AddVariable(new VariableMap("Beam Y", "Word", () => $"{_emulator.Vera.Beam_Y}"));

        scope = _scopeManager.GetScope("I2C", false);

        scope.AddVariable(new VariableMap("Previous Data", "uint", () => $"{(((_emulator.I2c.Previous & 1) != 0) ? "DATA" : "____")} {(((_emulator.I2c.Previous & 2) != 0) ? "CLK" : "___")}"));
        scope.AddVariable(new VariableMap("Direction", "uint", () => $"{(_emulator.I2c.ReadWrite == 0 ? "To SMC" : "From SMC")}"));
        scope.AddVariable(new VariableMap("Transmitting", "uint", () => $"0x{_emulator.I2c.Transmit:X2}"));
        scope.AddVariable(new VariableMap("Mode", "uint", () => $"{_emulator.I2c.Mode}"));
        scope.AddVariable(new VariableMap("Address", "uint", () => $"0x{_emulator.I2c.Address:X2}"));
        scope.AddVariable(new VariableMap("Data To Transmit", "bool", () => $"{_emulator.I2c.DataToTransmit != 0}"));

        scope = _scopeManager.GetScope("SMC", false);

        scope.AddVariable(new VariableMap("Data", "uint", () => _emulator.Smc.DataCount switch
        {
            0 => "Empty",
            1 => $"0x{_emulator.Smc.Data & 0xff:X2}",
            2 => $"0x{_emulator.Smc.Data & 0xff:X2} 0x{(_emulator.Smc.Data & 0xff00) >> 8:X2}",
            _ => $"0x{_emulator.Smc.Data & 0xff:X2} 0x{(_emulator.Smc.Data & 0xff00) >> 8:X2} 0x{(_emulator.Smc.Data & 0xff0000) >> 16:X2}",
        }));

        scope.AddVariable(new VariableMap("Last Offset", "uint", () => $"0x{_emulator.Smc.Offset:X2}"));
        scope.AddVariable(new VariableMap("LED", "uint", () => $"0x{_emulator.Smc.Led:X2}"));
        scope.AddVariable(new VariableMap("Keyb Read Position", "uint", () => $"0x{_emulator.Smc.SmcKeyboard_ReadPosition:X2}"));
        scope.AddVariable(new VariableMap("Keyb Write Position", "uint", () => $"0x{_emulator.Smc.SmcKeyboard_WritePosition:X2}"));
        scope.AddVariable(new VariableMap("Keyb No Data", "bool", () => $"{_emulator.Smc.SmcKeyboard_ReadNoData != 0}"));

        scope = _scopeManager.GetScope("VIA", false);

        scope.AddVariable(new VariableMap("A In Value ", "string", () => ViaByteDisplay(_emulator.Via.Register_A_InValue, '0', '1')));
        scope.AddVariable(new VariableMap("A Direction", "string", () => ViaByteDisplay(_emulator.Via.Register_A_Direction, '^', 'v')));
        scope.AddVariable(new VariableMap("A Out Value", "string", () => ViaByteDisplay(_emulator.Via.Register_A_OutValue, '0', '1')));
        scope.AddVariable(new VariableMap("A Value    ", "string", () => ViaByteDisplay(_emulator.Memory[0x9f01], '0', '1')));

        scope.AddVariable(new VariableMap("IO 0x9f00 PRB", "string", () => $"0b{Convert.ToString(_emulator.Memory[0x9f00], 2).PadLeft(8, '0')}"));
        scope.AddVariable(new VariableMap("IO 0x9f01 PRA", "string", () => $"0b{Convert.ToString(_emulator.Memory[0x9f01], 2).PadLeft(8, '0')}"));
        scope.AddVariable(new VariableMap("IO 0x9f02 DRB", "string", () => $"0b{Convert.ToString(_emulator.Memory[0x9f02], 2).PadLeft(8, '0')}"));
        scope.AddVariable(new VariableMap("IO 0x9f03 DRA", "string", () => $"0b{Convert.ToString(_emulator.Memory[0x9f03], 2).PadLeft(8, '0')}"));
        scope.AddVariable(new VariableMap("IO 0x9f04 T1L", "string", () => $"0b{Convert.ToString(_emulator.Memory[0x9f04], 2).PadLeft(8, '0')}"));
        scope.AddVariable(new VariableMap("IO 0x9f05 T1H", "string", () => $"0b{Convert.ToString(_emulator.Memory[0x9f05], 2).PadLeft(8, '0')}"));
        scope.AddVariable(new VariableMap("IO 0x9f06 L1L", "string", () => $"0b{Convert.ToString(_emulator.Memory[0x9f06], 2).PadLeft(8, '0')}"));
        scope.AddVariable(new VariableMap("IO 0x9f07 L1H", "string", () => $"0b{Convert.ToString(_emulator.Memory[0x9f07], 2).PadLeft(8, '0')}"));
        scope.AddVariable(new VariableMap("IO 0x9f08 T2L", "string", () => $"0b{Convert.ToString(_emulator.Memory[0x9f08], 2).PadLeft(8, '0')}"));
        scope.AddVariable(new VariableMap("IO 0x9f09 T2H", "string", () => $"0b{Convert.ToString(_emulator.Memory[0x9f09], 2).PadLeft(8, '0')}"));
        scope.AddVariable(new VariableMap("IO 0x9f0a SR ", "string", () => $"0b{Convert.ToString(_emulator.Memory[0x9f0a], 2).PadLeft(8, '0')}"));
        scope.AddVariable(new VariableMap("IO 0x9f0b ACR", "string", () => $"0b{Convert.ToString(_emulator.Memory[0x9f0b], 2).PadLeft(8, '0')}"));
        scope.AddVariable(new VariableMap("IO 0x9f0c PCR", "string", () => $"0b{Convert.ToString(_emulator.Memory[0x9f0c], 2).PadLeft(8, '0')}"));
        scope.AddVariable(new VariableMap("IO 0x9f0d IFR", "string", () => $"0b{Convert.ToString(_emulator.Memory[0x9f0d], 2).PadLeft(8, '0')}"));
        scope.AddVariable(new VariableMap("IO 0x9f0e IER", "string", () => $"0b{Convert.ToString(_emulator.Memory[0x9f0e], 2).PadLeft(8, '0')}"));
        scope.AddVariable(new VariableMap("IO 0x9f0f ORA", "string", () => $"0b{Convert.ToString(_emulator.Memory[0x9f0f], 2).PadLeft(8, '0')}"));
    }

    private static string ViaByteDisplay(byte input, char zeroValue, char oneValue)
    {
        var sb = new StringBuilder();

        sb.Append((input & 0b10000000) == 0 ? zeroValue : oneValue);
        sb.Append(' ');
        sb.Append((input & 0b01000000) == 0 ? zeroValue : oneValue);
        sb.Append(' ');
        sb.Append((input & 0b00100000) == 0 ? zeroValue : oneValue);
        sb.Append(' ');
        sb.Append((input & 0b00010000) == 0 ? zeroValue : oneValue);
        sb.Append(' ');
        sb.Append((input & 0b00001000) == 0 ? zeroValue : oneValue);
        sb.Append(' ');
        sb.Append((input & 0b00000100) == 0 ? zeroValue : oneValue);
        sb.Append(' ');
        sb.Append((input & 0b00000010) == 0 ? zeroValue : oneValue);
        sb.Append(' ');
        sb.Append((input & 0b00000001) == 0 ? zeroValue : oneValue);
        return sb.ToString();
    }

    private static string GetColourDepth(int inp) => inp switch
    {
        0 => "1",
        1 => "2",
        2 => "4",
        3 => "8",
        _ => "??"
    };

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
                Console.WriteLine($"*** Project Rom file not found: {_debugProject.RomFile}");
        }

        if (!File.Exists(rom))
        {
            Console.WriteLine($"*** Rom file not found: {rom}");
            throw new Exception($"Rom file not found {rom}");
        }

        Console.WriteLine($"Loading '{rom}'.");
        var romData = File.ReadAllBytes(rom);
        for (var i = 0; i < romData.Length; i++)
        {
            _emulator.RomBank[i] = romData[i];
        }
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
                Console.Write($"Loading Symbols {symbols.Name}... ");
                var bankData = _emulator.RomBank.Slice((symbols.RomBank ?? 0) * 0x4000, 0x4000).ToArray();
                _sourceMapManager.LoadSymbols(symbols);
                _sourceMapManager.LoadJumpTable(symbols.RangeDefinitions, 0xc000, symbols.RomBank ?? 0, bankData);

                Console.Write($"Decompiling... ");

                _disassemblerManager.DecompileRomBank(bankData, symbols.RomBank ?? 0);

                Console.WriteLine("Done.");
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

            Console.Write($"Decompiling Rom Bank {i}... ");

            _disassemblerManager.DecompileRomBank(bankData, i);

            Console.WriteLine("Done.");
        }

        if (_debugProject.KeyboardBuffer != null && _debugProject.KeyboardBuffer.Any()) {
            _emulator.Smc.SmcKeyboard_ReadNoData = 0;
            foreach(var i in _debugProject.KeyboardBuffer.Take(16))
                _emulator.SmcBuffer.PushByte(i);
        }

        try
        {
            var project = new Project();
            if (!string.IsNullOrWhiteSpace(_debugProject.Source))
            {
                Console.WriteLine($"Compiling {_debugProject.Source}");
                project.Code.Load(_debugProject.Source).GetAwaiter().GetResult();

                var compiler = new Compiler.Compiler(project);

                var compileResult = compiler.Compile().GetAwaiter().GetResult();

                _sourceMapManager.ConstructSourceMap(compileResult);

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

        _emulator = _getNewEmulatorInstance();
        _breakpointManager = new BreakpointManager(_emulator, this, _sourceMapManager, _idManager);
        _disassemblerManager = new DisassemblerManager(_sourceMapManager, _emulator, _idManager);
        _stackManager = new StackManager(_emulator, _idManager, _sourceMapManager, _disassemblerManager);
        _spriteManager = new SpriteManager(_emulator);
        _paletteManager = new PaletteManager(_emulator);

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
        Console.WriteLine("Starting emulator");
        while (_running)
        {
            var returnCode = _emulator.Emulate();

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
        var requestSize = arguments.Count + arguments.Offset ?? 0;
        var unreadCount = 0;
        if (requestSize > data.Length)
        {
            requestSize = data.Length - arguments.Offset ?? 0;
            unreadCount = arguments.Count - requestSize;
        }

        toReturn.UnreadableBytes = unreadCount;

        if (arguments.Offset <= data.Length)
        {
            toReturn.Data = Convert.ToBase64String(data.Slice(arguments.Offset ?? 0, requestSize));
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

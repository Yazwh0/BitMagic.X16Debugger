using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Emulator;

namespace BitMagic.X16Debugger;

// Not quite DI, but a place to hold all the managers and initialise in the correct order.
internal class ServiceManager
{
    private readonly Func<Emulator> _getNewEmulatorInstance;
    private readonly X16Debug _debugger;
    public Emulator Emulator { get; private set; }

    public BreakpointManager BreakpointManager { get; private set; }
    public ScopeManager ScopeManager { get; private set; }
    public SourceMapManager SourceMapManager { get; private set; }
    public VariableManager VariableManager { get; private set; }
    public StackManager StackManager { get; private set; }
    public SpriteManager SpriteManager { get; private set; }
    public PaletteManager PaletteManager { get; private set; }
    public PsgManager PsgManager { get; private set; }
    public DisassemblerManager DisassemblerManager { get; private set; }
    public ExpressionManager ExpressionManager { get; private set; }
    public CodeGeneratorManager CodeGeneratorManager { get; private set; }
    public DebugableFileManager DebugableFileManager { get; private set; }
    public BitmagicBuilder BitmagicBuilder { get; private set; }

    public IdManager IdManager { get; private set; }

#pragma warning disable CS8618
    public ServiceManager(Func<Emulator> GetNewEmulatorInstance, X16Debug debugger)
#pragma warning restore CS8618
    {
        _getNewEmulatorInstance = GetNewEmulatorInstance;
        _debugger = debugger;
        Reset();
    }

    public Emulator Reset()
    {
        Emulator = _getNewEmulatorInstance();
        Emulator.Brk_Causes_Stop = true; // todo: make this an option??

        IdManager = new();

        DebugableFileManager = new();

        SourceMapManager = new(Emulator);
        ScopeManager = new(IdManager);

        CodeGeneratorManager = new(IdManager);
        DisassemblerManager = new(SourceMapManager, Emulator, IdManager);
        BreakpointManager = new(Emulator, SourceMapManager, IdManager, DisassemblerManager, CodeGeneratorManager, DebugableFileManager);
        StackManager = new(Emulator, IdManager, SourceMapManager, DisassemblerManager);
        SpriteManager = new(Emulator);
        PaletteManager = new(Emulator);
        PsgManager = new(Emulator);
        VariableManager = new(IdManager, Emulator, ScopeManager, PaletteManager, SpriteManager, StackManager, PsgManager);
        ExpressionManager = new(VariableManager);
        BitmagicBuilder = new(DebugableFileManager, CodeGeneratorManager, _debugger.Logger);

        return Emulator;
    }
}

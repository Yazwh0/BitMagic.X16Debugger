using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.Scopes;
using BitMagic.X16Debugger.Variables;
using BitMagic.X16Emulator;

namespace BitMagic.X16Debugger;

// Not quite DI, but a place to hold all the managers and initialise in the correct order.
internal class ServiceManager
{
    private readonly Func<EmulatorOptions?, Emulator> _getNewEmulatorInstance;
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
    public ExceptionManager ExceptionManager { get; private set; }

    public IdManager IdManager { get; private set; }

#pragma warning disable CS8618
    public ServiceManager(Func<EmulatorOptions?, Emulator> GetNewEmulatorInstance, X16Debug debugger)
#pragma warning restore CS8618
    {
        _getNewEmulatorInstance = GetNewEmulatorInstance;
        _debugger = debugger;
        Reset();
    }

    public Emulator Reset()
    {
        Emulator = _getNewEmulatorInstance(null);

        IdManager = new();

        DebugableFileManager = new(IdManager);

        SourceMapManager = new(Emulator, _debugger.Logger);
        ScopeManager = new(IdManager);
        ExceptionManager = new(Emulator, IdManager);

        CodeGeneratorManager = new(IdManager);
        DisassemblerManager = new(SourceMapManager, Emulator, IdManager);
        BreakpointManager = new(Emulator, IdManager, DisassemblerManager, DebugableFileManager);
        DebugableFileManager.SetBreakpointManager(BreakpointManager);
        DisassemblerManager.SetBreakpointManager(BreakpointManager);
        StackManager = new(Emulator, IdManager, SourceMapManager, DisassemblerManager, DebugableFileManager);
        SpriteManager = new(Emulator);
        PaletteManager = new(Emulator);
        PsgManager = new(Emulator);
        VariableManager = new(IdManager, Emulator, ScopeManager, PaletteManager, SpriteManager, StackManager, PsgManager);
        ExpressionManager = new(VariableManager, Emulator);
        BitmagicBuilder = new(DebugableFileManager, CodeGeneratorManager, _debugger.Logger);

        VariableManager.SetExpressionManager(ExpressionManager);
        BreakpointManager.SetExpressionManager(ExpressionManager);

        BreakpointManager.BreakpointsUpdated += _debugger.BreakpointManager_BreakpointsUpdated;


        return Emulator;
    }

}

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
    public DisassemblerManager DisassemblerManager { get; private set; }
    public ExpressionManager ExpressionManager { get; private set; }
    public CodeGeneratorManager CodeGeneratorManager { get; private set; }

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

        IdManager = new IdManager();

        SourceMapManager = new SourceMapManager(IdManager);
        ScopeManager = new ScopeManager(IdManager);

        CodeGeneratorManager = new CodeGeneratorManager(IdManager);
        DisassemblerManager = new DisassemblerManager(SourceMapManager, Emulator, IdManager);
        BreakpointManager = new BreakpointManager(Emulator, _debugger, SourceMapManager, IdManager, DisassemblerManager, CodeGeneratorManager);
        StackManager = new StackManager(Emulator, IdManager, SourceMapManager, DisassemblerManager);
        SpriteManager = new SpriteManager(Emulator);
        PaletteManager = new PaletteManager(Emulator);
        VariableManager = new VariableManager(IdManager, Emulator, ScopeManager, PaletteManager, SpriteManager, StackManager);
        ExpressionManager = new ExpressionManager(VariableManager);

        return Emulator;
    }
}

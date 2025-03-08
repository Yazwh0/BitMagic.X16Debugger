using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.Scopes;
using BitMagic.X16Debugger.Variables;
using BitMagic.X16Emulator;
using System.Drawing;

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

        //var colours = Emulator.DebugSpriteColours;

        //var xidx = 1;
        //foreach (var c in GetGradients(0x800000ff, 0x8000ffff, 21))
        //{
        //    colours[xidx++] = c;
        //}
        //foreach (var c in GetGradients(0x8000ffff, 0x8000ff00, 21))
        //{
        //    colours[xidx++] = c;
        //}
        //foreach (var c in GetGradients(0x8000ff00, 0x80ffff00, 21))
        //{
        //    colours[xidx++] = c;
        //}
        //foreach (var c in GetGradients(0x80ffff00, 0x80ff0000, 22))
        //{
        //    colours[xidx++] = c;
        //}
        //foreach (var c in GetGradients(0x80ff0000, 0x80ff00ff, 21))
        //{
        //    colours[xidx++] = c;
        //}
        //foreach (var c in GetGradients(0x80ff00ff, 0x800000ff, 22))
        //{
        //    colours[xidx++] = c;
        //}

        //for (var i = 0; i < 128; i++)
        //{
        //    var c = colours[i];

        //    Console.WriteLine($"0x{c:X8}, ");
        //}

        return Emulator;
    }


    private static IEnumerable<uint> GetGradients(uint start_val, uint end_val, int steps)
    {
        var start = Color.FromArgb((int)start_val);
        var end = Color.FromArgb((int)end_val);

        int stepA = ((end.A - start.A) / (steps - 1));
        int stepR = ((end.R - start.R) / (steps - 1));
        int stepG = ((end.G - start.G) / (steps - 1));
        int stepB = ((end.B - start.B) / (steps - 1));

        for (int i = 0; i < steps; i++)
        {
            yield return (uint)Color.FromArgb(start.A + (stepA * i),
                                        start.R + (stepR * i),
                                        start.G + (stepG * i),
                                        start.B + (stepB * i)).ToArgb();
        }
    }
}

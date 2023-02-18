using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using System.Globalization;

namespace BitMagic.X16Debug;

public class X16Debug : DebugAdapterBase
{
    private readonly Emulator _emulator;
    private int nextId = 999;

    private readonly SampleScope _globals;

    // This will be started on a second thread, seperate to the emulator
    public X16Debug(Emulator emulator, Stream stdIn, Stream stdOut)
    {
        _emulator = emulator;
        _globals = SetupGlobalObjects();

        InitializeProtocolClient(stdIn, stdOut);
    }

    private SampleScope SetupGlobalObjects()
    {
        var globals = new SampleScope(this, "Globals", false);

        // todo: add line number position
        globals.AddVariable(new WrapperVariable(this, "CurrentLine", "int", () => 1.ToString(CultureInfo.InvariantCulture)));
        globals.AddVariable(new WrapperVariable(this, "Line", "string", () => "Todo"));

        var cpu = new SimpleVariable(this, "CPU", null, null);
        cpu.AddChild(new WrapperVariable(this, "A", "Register", () => $"0x{_emulator.A:X2}"));
        cpu.AddChild(new WrapperVariable(this, "X", "Register", () => $"0x{_emulator.X:X2}"));
        cpu.AddChild(new WrapperVariable(this, "Y", "Register", () => $"0x{_emulator.Y:X2}"));
        cpu.AddChild(new WrapperVariable(this, "PC", "Register", () => $"0x{_emulator.Pc:X4}"));

        globals.AddVariable(cpu);

        return globals;
    }

    internal int GetNextId()
    {
        return Interlocked.Increment(ref this.nextId);
    }

    // run or step an opcode
    private void Continue(bool step)
    { 
    }

    public void Run()
    {
        this.Protocol.Run();
    }

    #region Initialize/Disconnect

    protected override InitializeResponse HandleInitializeRequest(InitializeArguments arguments)
    {
 //       if (arguments.LinesStartAt1 == true)
//            this.clientsFirstLine = 1;

        this.Protocol.SendEvent(new InitializedEvent());

        return new InitializeResponse() {
            //SupportsEvaluateForHovers = true,
            //SupportsExceptionOptions = true,
            //SupportsConfigurationDoneRequest = true
        };
    }

    protected override LaunchResponse HandleLaunchRequest(LaunchArguments arguments)
    {
        return new LaunchResponse();
    }

    protected override DisconnectResponse HandleDisconnectRequest(DisconnectArguments arguments)
    {
        return new DisconnectResponse();
    }

    #endregion

    #region Breakpoints

    protected override SetBreakpointsResponse HandleSetBreakpointsRequest(SetBreakpointsArguments arguments)
    {
        return new SetBreakpointsResponse();
    }

    #endregion

    #region Continue/Stepping

    protected override ConfigurationDoneResponse HandleConfigurationDoneRequest(ConfigurationDoneArguments arguments)
    {
        return new ConfigurationDoneResponse();
    }

    protected override ContinueResponse HandleContinueRequest(ContinueArguments arguments)
    {
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
        return new NextResponse();
    }

    #endregion
}
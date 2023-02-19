using BitMagic.Compiler;
using BitMagic.X16Debugger;
using BitMagic.X16Emulator;
using BitMagic.X16Emulator.Display;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Utilities;
using Newtonsoft.Json;
using System.Globalization;
using System.Text;
using SysThread = System.Threading.Thread;

namespace BitMagic.X16Debugger;

public class X16Debug : DebugAdapterBase
{
    private readonly Emulator _emulator;
    private int nextId = 999;

    private readonly SampleScope _globals;

    private SysThread? _debugThread;

    // managers
    private readonly ExceptionManager _exceptionManager;

    //private DirectiveProcessor directiveProcessor;
    private bool _running = true;

    // This will be started on a second thread, seperate to the emulator
    public X16Debug(Emulator emulator, Stream stdIn, Stream stdOut)
    {
        _emulator = emulator;
        _globals = SetupGlobalObjects();

        _exceptionManager = new ExceptionManager(this);


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

    public SysThread? DebugThread => _debugThread;

    internal int GetNextId()
    {
        return Interlocked.Increment(ref this.nextId);
    }

    // run or step an opcode
    private void Continue(bool step)
    {
    }

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

        var code = File.ReadAllText(toCompile);
        var compiler = new Compiler.Compiler(code);
        try
        {
            var compileResult = compiler.Compile().GetAwaiter().GetResult();

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
            Console.WriteLine($"Done. {prg.Length:#,##0} bytes.");
        }
        catch (Exception e)
        {
            throw new ProtocolException(e.Message);
        }

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

    protected override SetExceptionBreakpointsResponse HandleSetExceptionBreakpointsRequest(SetExceptionBreakpointsArguments arguments)
       => _exceptionManager.HandleSetExceptionBreakpointsRequest(arguments);

    #endregion

    #region Continue/Stepping

    // Startup
    protected override ConfigurationDoneResponse HandleConfigurationDoneRequest(ConfigurationDoneArguments arguments)
    {
        _debugThread = new SysThread(DebugLoop);
        _debugThread.Name = "Debug Look Thread";
        _debugThread.Start();

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

    #region Debug Loop

    // Run the emulator, handle stops, breakpoints, etc.
    private void DebugLoop()
    {
        EmulatorWindow.Run(_emulator);

        while (_running)
        {
            Console.WriteLine("Starting emulator");
            var returnCode = _emulator.Emulate();

            switch (returnCode)
            {
                case Emulator.EmulatorResult.Stepping:
                    break;
                case Emulator.EmulatorResult.DebugOpCode:
                    break;
                default:
                    Console.WriteLine($"Stopping with because of {returnCode} result");
                    _running = false;
                    break;
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
                    Id= 0,
                    Name = "CX16"
                }
            });

    #endregion
}
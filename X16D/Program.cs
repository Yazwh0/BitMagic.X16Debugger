using BitMagic.X16Debugger;
using BitMagic.X16Emulator;
using CommandLine;
using System.Net.Sockets;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;

namespace X16D;

static class Program
{

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool SetDllDirectory(string lpPathName);

    internal class Options
    {
        [Option("debug", Default = false, Required = false)]
        public bool Debug { get; set; }

        [Option("nodebug", Default = false, Required = false)]
        public bool NoDebug { get; set; }

        [Option("port", Default = 0, Required = false)]
        public int ServerPort { get; set; }

        [Option("stepOnEnter", Default = false, Required = false)]
        public bool StepOnEnter { get; set; }

        [Option("officialEmulator", Default = "", Required = false)]
        public string OfficialEmulatorLocation { get; set; } = "";
    }

    private const string RomEnvironmentVariable = "BITMAGIC_ROM";

    static int Main(string[] args)
    {
        Console.WriteLine("BitMagic - X16D");

        ParserResult<Options>? argumentsResult = null;
        try
        {
            argumentsResult = Parser.Default.ParseArguments<Options>(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error processing arguments:");
            Console.WriteLine(ex.Message);
        }

        var options = argumentsResult?.Value ?? new Options() { ServerPort = 2563 };

        var rom = "rom.bin";

        if (!File.Exists(rom))
        {
            var env = Environment.GetEnvironmentVariable(RomEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(env))
            {
                rom = env;

                if (!File.Exists(rom))
                {
                    rom = @$"{env}\rom.bin";
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(options.OfficialEmulatorLocation))
        {
            if (Directory.Exists(options.OfficialEmulatorLocation))
                Console.WriteLine($"Emulator: {options.OfficialEmulatorLocation}");
            else
                Console.WriteLine($"Emulator does not exist: {options.OfficialEmulatorLocation}");
        }

        if (!File.Exists("EmulatorCore.dll") && !File.Exists("EmulatorCore.so"))
        {
            Console.WriteLine($"Cannot find EmulatorCode.dll or .so in cwd '{Directory.GetCurrentDirectory()}'");
            var newLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (newLocation == null)
            {
                Console.WriteLine("Path for current executable is null!");
                return 1;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Console.WriteLine($"Calling SetDllDirectory with '{newLocation}'");
                SetDllDirectory(newLocation);
            }
        }

        Func<EmulatorOptions?, Emulator> getEmulator = (options) =>
        {
            var emulator = new Emulator(options);

            emulator.FrameControl = FrameControl.Synced;
            emulator.Stepping = true;

            return emulator;
        };

        try
        {
            if (options.ServerPort != 0)
                RunAsServer(getEmulator, options.ServerPort, rom, options.OfficialEmulatorLocation);
            else
            {
                Console.WriteLine(@"Running using stdin\stdout.");

                var debugger = new X16Debug(getEmulator, Console.OpenStandardInput(), Console.OpenStandardOutput(), rom, options.OfficialEmulatorLocation);
                try
                {
                    //debugger.Protocol.LogMessage += (_, e) => Debug.WriteLine(e.Message);
                    debugger.Logger.LogLine("Starting");
                    debugger.Run();
                    debugger.Logger.LogLine("Finished. (Normally)");
                }
                catch (Exception e)
                {
                    debugger.Logger.LogError(e.Message);
                    debugger.Logger.LogLine("Finished. (Error)");

                    throw;
                }
            }
        }
        catch (Exception e)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), $"bitmagic_crash_{DateTime.Now:yyyyMMdd_HHmmss}.txt"),
                e.Message + Environment.NewLine + e.StackTrace);

            return 1;
        }


        Console.WriteLine("Exiting.");
        return 0;
    }

    private static void RunAsServer(Func<EmulatorOptions?, Emulator> getEmulator, int port, string rom, string emulatorLocation)
    {
        Console.WriteLine($"Listening on port {port}.");
        X16Debug? debugger;

        Thread listenThread = new Thread(() =>
        {
            TcpListener listener = new TcpListener(IPAddress.Parse("127.0.0.1"), port);
            listener.Start();

            while (true)
            {
                Socket clientSocket = listener.AcceptSocket();
                Thread clientThread = new Thread(() =>
                {
                    Console.WriteLine("Accepted connection");

                    using (Stream stream = new NetworkStream(clientSocket))
                    {
                        var logger = new ConsoleLogger();
                        debugger = new X16Debug(getEmulator, stream, stream, rom, emulatorLocation, logger);
                        logger.AddSecondaryLogger(new DebugLogger(debugger));

                        debugger.Protocol.DispatcherError += (sender, e) =>
                        {
                            Console.Error.WriteLine(e.Exception.Message);
                        };
                        debugger.Run();


                        debugger = null;
                    }

                    Console.WriteLine("Connection closed");
                });

                clientThread.Name = "DebugServer connection thread";
                clientThread.Start();
            }
        });

        listenThread.Name = "DebugServer listener thread";
        listenThread.Start();
        listenThread.Join();
    }
}
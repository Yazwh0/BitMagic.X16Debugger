using BitMagic.Common;
using BitMagic.X16Debugger;
using BitMagic.X16Emulator;
using CommandLine;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net;
using static X16D.Program;
using System.Text;

namespace X16D;

static class Program
{
    // placeholder from the sample
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
    }

    private const string RomEnvironmentVariable = "BITMAGIC_ROM";

    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("BitMagic - X16D");

        ParserResult<Options>? argumentsResult  = null;
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

        Func<Emulator> getEmulator = () =>
        {
            var emulator = new Emulator();

            emulator.Brk_Causes_Stop = false;
            emulator.FrameControl = FrameControl.Synced;
            emulator.Stepping = true;

            SdCard sdCard = new SdCard(16);
            emulator.LoadSdCard(sdCard);

            return emulator;
        };

        if (options.ServerPort != 0)
            RunAsServer(getEmulator, options.ServerPort, rom);
        else
        {
            Console.WriteLine(@"Running using stdin\stdout.");
            
            var debugger = new X16Debug(getEmulator, Console.OpenStandardInput(), Console.OpenStandardOutput(), rom);
            debugger.Protocol.LogMessage += (_, e) => Debug.WriteLine(e.Message);
            debugger.Run();
        }

        Console.WriteLine(@"Exiting.");
        return 0;
    }

    private static void RunAsServer(Func<Emulator> getEmulator, int port, string rom)
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
                        debugger = new X16Debug(getEmulator, stream, stream, rom, new ConsoleLogger());
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
using BitMagic.X16Debugger;
using BitMagic.X16Debugger.LSP;
using BitMagic.X16Emulator;
using CommandLine;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Thread = System.Threading.Thread;

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

        [Option("dapport", Default = 0, Required = false)]
        public int DapServerPort { get; set; }
        
        [Option("lspport", Default = 2564, Required = false)]
        public int LspServerPort { get; set; }

        [Option("stepOnEnter", Default = false, Required = false)]
        public bool StepOnEnter { get; set; }

        [Option("officialEmulator", Default = "", Required = false)]
        public string OfficialEmulatorLocation { get; set; } = "";

        [Option("runInOfficialEmulator", Default = false, Required = false)]
        public bool RunInOfficialEmulator { get; set; }

        [Option("officialEmulatorParameters", Default = "", Required = false)]
        public string OfficialEmulatorParameters { get; set; } = "";
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

        var options = argumentsResult?.Value ?? new Options() { DapServerPort = 2563 };

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
            if (options.DapServerPort != 0)
                RunAsServer(getEmulator, options.DapServerPort, options.LspServerPort, rom, options.OfficialEmulatorLocation, options.RunInOfficialEmulator, options.OfficialEmulatorParameters);
            else
            {
                Console.WriteLine(@"Running using stdin\stdout.");

                var debugger = new X16Debug(getEmulator, Console.OpenStandardInput(), Console.OpenStandardOutput(), rom, options.OfficialEmulatorLocation, options.RunInOfficialEmulator, options.OfficialEmulatorParameters);
                try
                {
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

    private static void RunAsServer(Func<EmulatorOptions?, Emulator> getEmulator, int dapPort, int lspPort, string rom, string emulatorLocation, bool runInEmulatorLocation, string officialEmulatorParameters)
    {
        Console.WriteLine($"DAP Listening on port {dapPort}.");
        Console.WriteLine($"LSP Listening on port {lspPort}.");
        X16Debug? debugger;
        LspServer? lspServer;

        var listenThread = new Thread(() =>
        {
            var listener = new TcpListener(IPAddress.Parse("127.0.0.1"), dapPort);
            listener.Start();

            while (true)
            {
                var clientSocket = listener.AcceptSocket();
                var inputStream = new NetworkStream(clientSocket);

                var clientThread = new Thread(() =>
                {
                    Console.WriteLine("DAP Accepted connection");

                    var logger = new ConsoleLogger();
                    debugger = new X16Debug(getEmulator, inputStream, inputStream, rom, emulatorLocation, runInEmulatorLocation, officialEmulatorParameters, logger);
                    logger.AddSecondaryLogger(new DebugLogger(debugger));

                    debugger.Protocol.DispatcherError += (sender, e) =>
                    {
                        Console.Error.WriteLine(e.Exception.Message);
                    };
                    debugger.Run();
                    debugger = null;

                    Console.WriteLine("DAP Connection closed");
                });

                clientThread.Name = "DebugServer connection thread";
                clientThread.Start();
            }
        });

        Thread lspListenThread = new Thread(async () =>
        {
            var listener = new TcpListener(IPAddress.Parse("127.0.0.1"), lspPort);
            listener.Start();
            try
            {
                while (true)
                {
                    var clientSocket = listener.AcceptSocket();
                    var inputStream = new NetworkStream(clientSocket);

                    lspServer = new LspServer(inputStream, inputStream);
                    lspServer.Run();

                    Console.WriteLine("LSP Connection closed");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LSP Exception: {ex.Message}");
            }
        });

        lspListenThread.Name = "LSP listener thread";
        lspListenThread.Start();

        listenThread.Name = "DebugServer listener thread";
        listenThread.Start();
        listenThread.Join();
    }
}

public class StreamProxy
{
    public BlockingMemoryStream DapStream { get; } = new BlockingMemoryStream();
    public BlockingMemoryStream LspStream { get; } = new BlockingMemoryStream();

    private char[] Buffer = new char[1024 * 10];
    private readonly Encoding _encoding = Encoding.UTF8;

    public void Start(Stream Source)
    {
        using var reader = new StreamReader(Source, _encoding);
        using var dapWriter = new StreamWriter(DapStream);
        using var lspWriter = new StreamWriter(LspStream);

        string? line;

        ReadOnlySpan<char> span = Buffer;

        while (true)
        {
            int contentLength = 0;
            while (!string.IsNullOrEmpty(line = reader.ReadLine()))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    contentLength = int.Parse(line.Substring("Content-Length:".Length).Trim());
                }
            }

            if (contentLength == 0) continue;

            if (Buffer.Length < contentLength)
                Buffer = new char[contentLength];

            // Read content
            int totalRead = 0;
            while (totalRead < contentLength)
            {
                int read = reader.Read(Buffer, totalRead, contentLength - totalRead);
                if (read == 0) break; // End of stream
                totalRead += read;
            }

            string header = $"Content-Length: {contentLength}\r\n\r\n";
            byte[] headerBytes = _encoding.GetBytes(header);

            var test = span.ToString();

            DapStream.Write(headerBytes, 0, headerBytes.Length);
            DapStream.Write(_encoding.GetBytes(Buffer, 0, contentLength), 0, contentLength);
            DapStream.Flush();
        }

        DapStream.Flush();
        lspWriter.Flush();
    }
}

public class BlockingMemoryStream : Stream
{
    private readonly ConcurrentQueue<byte[]> _buffers = new();
    private readonly ManualResetEventSlim _dataReady = new(false);

    public void WriteData(byte[] data)
    {
        _buffers.Enqueue(data);
        _dataReady.Set();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        _dataReady.Wait(); // Block until data is available

        if (_buffers.TryDequeue(out var data))
        {
            int bytesToCopy = Math.Min(count, data.Length);
            Array.Copy(data, 0, buffer, offset, bytesToCopy);

            if (_buffers.IsEmpty)
                _dataReady.Reset();

            return bytesToCopy;
        }

        return 0;
    }

    // Required overrides (minimal implementation)
    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count)
    {
        var data = new byte[count];
        Array.Copy(buffer, offset, data, 0, count);
        WriteData(data);
    }
}

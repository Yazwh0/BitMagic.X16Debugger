using BitMagic.Common;
using BitMagic.X16Debugger.Builder;
using BitMagic.X16Debugger.LSP.Logging;
using BitMagic.X16Emulator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Server;
using static BitMagic.X16Debugger.LSP.PreviewHandler;

namespace BitMagic.X16Debugger.LSP;

public class LspServer(Stream inputStream, Stream outputStream)
{
    private LanguageServer? _server;

    public async Task Run()
    {
        _server = await LanguageServer.From(options =>
        {
            options.Services
                .AddSingleton<DocumentCache>()
                .AddSingleton<TokenDescriptionService>()
                .AddSingleton<ITokenDescriptionProvider, X16KernelDocumentation>()
                .AddSingleton<ProjectService>()
                .AddSingleton<ProjectBuilder>()
                .AddSingleton<ServiceManager>(e => new ServiceManager(GetEmulator, e.GetService<IEmulatorLogger>()))
                .AddSingleton<FileChangeHandler>()
                .AddSingleton<PreviewHandler>()
                .AddSingleton<ClientNotificationService>()
                .AddSingleton<IEmulatorLogger, Logger>()
                ;

            options
                //.WithInput(new LoggingStreamWrapper(inputStream, "input"))
                //.WithOutput(new LoggingStreamWrapper(outputStream, "output"))
                .WithInput(inputStream)
                .WithOutput(outputStream)
                .WithHandler<PreviewHandler>()
                .WithHandler<HoverHandler>()
                .WithHandler<FileChangeHandler>()
                .WithLoggerFactory(new LogFactory())
                .OnRequest<PreviewParameters, PreviewResult>("bitmagic/preview", async (request, ct) => await HandlePreviewRequest(request, ct))
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Trace); // Trace logs everything
                })
                .OnInitialize(OnInitialise)
                ;
        });

        ServiceManagerFactory.SetServiceManager(_server.Services.GetRequiredService<ServiceManager>());

        var cns = _server.Services.GetRequiredService<ClientNotificationService>();
        cns.SetLanguageServer(_server);

        var fch = _server.Services.GetRequiredService<FileChangeHandler>();
        fch.SetLanguageServer(_server);

        DocumentCache.Instance = _server.Services.GetRequiredService<DocumentCache>();

        await _server.WaitForExit;
        
    }

    private readonly Func<EmulatorOptions?, Emulator> GetEmulator = (options) =>
    {
        var emulator = new Emulator(options);

        emulator.FrameControl = FrameControl.Synced;
        emulator.Stepping = true;

        return emulator;
    };

    private Task<PreviewResult> HandlePreviewRequest(PreviewParameters parameters, CancellationToken cancellationToken)
    {
        if (_server == null)
            throw new InvalidOperationException("LSP Server is not initialized.");

        var hander = _server.Services.GetService<PreviewHandler>() ?? throw new Exception();

        return hander.Handle(parameters, cancellationToken);
    }

    private async Task OnInitialise(ILanguageServer server, InitializeParams request, CancellationToken token)
    {
        var documentCache = server.Services.GetRequiredService<DocumentCache>();
        var projectSerivce = server.Services.GetRequiredService<ProjectService>();
        var fileChangeHandler = server.Services.GetRequiredService<FileChangeHandler>();

        if (request.WorkspaceFolders == null)
            return;

        var projectFile = "";
        var workspaceFolder = "";

        FileCache.Clear();

        foreach (var folder in request.WorkspaceFolders)
        {
            Console.WriteLine($"Workspace Folder: {folder.Name} - {folder.Uri}");
            if (projectFile != null)
            {
                foreach (var file in Directory.EnumerateFiles(folder.Uri.GetFileSystemPath(), "project.json", SearchOption.AllDirectories))
                {
                    Console.WriteLine($"Project File : {file}");
                    projectFile = file.FixFilename();
                    workspaceFolder = folder.Uri.GetFileSystemPath().FixFilename();
                    break;
                }
            }

            // look for all bmasm files.
            foreach (var file in Directory.EnumerateFiles(folder.Uri.GetFileSystemPath(), "*.bmasm", SearchOption.AllDirectories))
            {
                Console.WriteLine($"File : {file}");
                await documentCache.AddFile(file.FixFilename());
            }
        }

        if (string.IsNullOrEmpty(projectFile))
        {
            Console.WriteLine("LSP Server Initialized");
            return;
        }

        projectSerivce.SetProject(X16DebugProject.Load(projectFile, workspaceFolder), workspaceFolder);

        fileChangeHandler.QueueUpdateFileChanges(); // initial build
    }
}

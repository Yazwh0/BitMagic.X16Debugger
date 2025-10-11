using BitMagic.X16Debugger.LSP.Logging;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using OmniSharp.Extensions.LanguageServer.Server;
using SixLabors.ImageSharp;

namespace BitMagic.X16Debugger.LSP;

public class LspServer(Stream inputStream, Stream outputStream)
{
    public void Run()
    {
        var server = LanguageServer.From(options =>
        {
            options.Services
                .AddSingleton<DocumentCache>()
                .AddSingleton<TokenDescriptionService>()
                .AddSingleton<ITokenDescriptionProvider, X16KernelDocumentation>()
                ;

            options
                //.WithInput(new LoggingStreamWrapper(inputStream, "input"))
                //.WithOutput(new LoggingStreamWrapper(outputStream, "output"))
                .WithInput(inputStream)
                .WithOutput(outputStream)
                .WithHandler<HoverHandler>()
                .WithHandler<FileChangeHandler>()
                .WithLoggerFactory(new LogFactory())
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Debug); // Trace logs everything
                })
                .OnInitialize(OnInitialise)
                ;


        }).GetAwaiter().GetResult();

        server.WaitForExit.GetAwaiter().GetResult();
    }

    private async Task OnInitialise(ILanguageServer server, InitializeParams request, CancellationToken token)
    {
        var documentCache = server.Services.GetRequiredService<DocumentCache>();

        if (request.WorkspaceFolders == null)
            return;

        var projectFile = "";

        // todo: change this to use the project.json
        foreach (var folder in request.WorkspaceFolders)
        {
            Console.WriteLine($"Workspace Folder: {folder.Name} - {folder.Uri}");
            if (projectFile != null)
            {
                foreach (var file in Directory.EnumerateFiles(folder.Uri.GetFileSystemPath(), "project.json", SearchOption.AllDirectories))
                {
                    Console.WriteLine($"Project File : {file}");
                    projectFile = file;
                    break;
                }
            }

            // look for all bmasm files.
            foreach (var file in Directory.EnumerateFiles(folder.Uri.GetFileSystemPath(), "*.bmasm", SearchOption.AllDirectories))
            {
                Console.WriteLine($"File : {file}");
                await documentCache.AddFile(file);
            }
        }

        Console.WriteLine("LSP Server Initialized");
    }
}

internal class FileChangeHandler(DocumentCache documentCache) : IDidChangeTextDocumentHandler
{
    public TextDocumentChangeRegistrationOptions GetRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
    {
        return new TextDocumentChangeRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("bmasm"),
            SyncKind = TextDocumentSyncKind.Incremental
        };
    }

    public Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        var filename = request.TextDocument.Uri.GetFileSystemPath();
        if (!documentCache.IsInCache(filename))
            return Unit.Task;

        var lines = documentCache.GetFile(filename);

        foreach (var change in request.ContentChanges)
        {
            if (change.Range == null)
            {
                lines = change.Text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            }
            else
            {
                lines = ApplyIncrementalChange(lines, change);
            }
        }

        documentCache.SetFileContent(filename, lines);

        //Console.WriteLine("-----");
        //foreach (var line in lines)
        //    Console.WriteLine(line);
        //Console.WriteLine("-----");

        return Unit.Task;
    }

    private string[] ApplyIncrementalChange(string[] originalLines, TextDocumentContentChangeEvent change)
    {
        var range = change.Range!;
        var start = range.Start;
        var end = range.End;

        var before = originalLines.Take(start.Line);
        var after = originalLines.Skip(end.Line + 1);
        var affected = originalLines[(start.Line)..(end.Line + 1)];

        var updated = before
                .Concat(Replace(affected, change))
                .Concat(after).ToArray();

        return updated;
    }

    private IEnumerable<string> Replace(string[] original, TextDocumentContentChangeEvent change)
    {
        // assume first line is the line of the change
        var newTextLines = change.Text.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);

        if (newTextLines.Length == 0) // full replace
            yield break;

        if (newTextLines.Length == 1) // replace part of a line
        {
            yield return original[0][..(change.Range!.Start.Character)] +
                         newTextLines[0] +
                         original[^1][(change.Range!.End.Character)..];
            yield break;
        }

        // change the first line as required
        yield return original[0][..(change.Range!.Start.Character)] + newTextLines[0];

        if (newTextLines.Length > 2)
        {
            foreach (var line in newTextLines[1..^1]) // ignore first and last
            {
                yield return line;
            }
        }

        // change the last line as required
        yield return newTextLines[^1] + original[^1][(change.Range!.End.Character)..];
    }
}

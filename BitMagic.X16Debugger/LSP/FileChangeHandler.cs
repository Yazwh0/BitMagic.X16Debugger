using BitMagic.Common;
using BitMagic.Compiler.Exceptions;
using BitMagic.TemplateEngine.Compiler;
using BitMagic.X16Debugger.Builder;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using OmniSharp.Extensions.LanguageServer.Server;
using SixLabors.ImageSharp;
using System.Diagnostics;

namespace BitMagic.X16Debugger.LSP;

internal class FileChangeHandler(DocumentCache documentCache, ProjectBuilder projectBuilder,
    ClientNotificationService clientNotificationService, ServiceManager serviceManager, ProjectService projectService) : IDidChangeTextDocumentHandler
{
    private readonly Debouncer debouncer = new Debouncer();
    private LanguageServer? languageServer = null;
    private HashSet<string> filesWithErrors = new HashSet<string>();

    public void SetLanguageServer(LanguageServer server)
    {
        languageServer = server;
    }

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
        var filename = request.TextDocument.Uri.GetFileSystemPath().FixFilename();
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

        Console.WriteLine("-----");
        foreach (var line in lines)
            Console.WriteLine(line);
        Console.WriteLine("-----");

        debouncer.Debounce(UpdateFileChanges);

        return Unit.Task;
    }

    internal void QueueUpdateFileChanges()
    {
        debouncer.Debounce(UpdateFileChanges);
    }

    internal async Task UpdateFileChanges()
    {
        try
        {
            if (languageServer == null)
                throw new FileChangedHandlerNotInitialisedException();

            await projectBuilder.Build();

            clientNotificationService.SendNotfication("bitmagic/generatedchange", new GeneratedFileChangesParameters()
            {
                Filenames = serviceManager.DebugableFileManager.AllFilenames().Where(i => i.EndsWith(".generated.bmasm")).ToArray()
            });

            foreach (var i in filesWithErrors)
            {
                languageServer.SendNotification<PublishDiagnosticsParams>("textDocument/publishDiagnostics", new PublishDiagnosticsParams()
                {
                    Uri = DocumentUri.FromFileSystemPath(i),
                    Diagnostics = [],
                });
            }

            filesWithErrors.Clear();
        }
        catch (CompilerLineException e)
        {
            if (languageServer == null)
                throw new FileChangedHandlerNotInitialisedException();

            var sourceFile = e.Line.Source.SourceFile;
            var lineNumber = e.Line.Source.LineNumber - 1;
            serviceManager.DebugableFileManager.AddFiles(sourceFile);

            var wrapper = serviceManager.DebugableFileManager.GetWrapper(sourceFile) ?? throw new Exception("Cannot find source file!");

            var ul = wrapper.FindUltimateSource(lineNumber, serviceManager.DebugableFileManager);

            string path;
            if (sourceFile.Path.EndsWith(".generated.bmasm"))
                path = "bitmagic:generated/" + sourceFile.Path;
            else
                path = sourceFile != null ? Path.Combine(projectService.Project.BasePath, sourceFile.Path) : "";

            var toSend = new List<Diagnostic>() {
                new Diagnostic()
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = e.Message,
                    Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(ul.lineNumber, 0), new Position(ul.lineNumber, 1000)),
                    Source = "BitMagic Template Engine",
                }
            };

            filesWithErrors.Add(path);

            languageServer.SendNotification<PublishDiagnosticsParams>("textDocument/publishDiagnostics", new PublishDiagnosticsParams()
            {
                Uri = DocumentUri.FromFileSystemPath(path),
                Diagnostics = toSend,
            });
        }
        catch (CompilerSourceException e)
        {
            if (languageServer == null)
                throw new FileChangedHandlerNotInitialisedException();

            var sourceFile = e.SourceFile.SourceFile;
            var lineNumber = e.SourceFile.LineNumber - 1;
            serviceManager.DebugableFileManager.AddFiles(sourceFile);

            var wrapper = serviceManager.DebugableFileManager.GetWrapper(sourceFile) ?? throw new Exception("Cannot find source file!");

            var ul = wrapper.FindUltimateSource(lineNumber, serviceManager.DebugableFileManager);

            string path;
            if (sourceFile.Path.EndsWith(".generated.bmasm"))
                path = "bitmagic:generated/" + sourceFile.Path;
            else
                path = sourceFile != null ? Path.Combine(projectService.Project.BasePath, sourceFile.Path) : "";

            var toSend = new List<Diagnostic>();
            toSend.Add(new Diagnostic()
            {
                Severity = DiagnosticSeverity.Error,
                Message = e.Message,
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(ul.lineNumber, 0), new Position(ul.lineNumber, 1000)),
                Source = "BitMagic Template Engine",
            });

            filesWithErrors.Add(path);

            languageServer.SendNotification<PublishDiagnosticsParams>("textDocument/publishDiagnostics", new PublishDiagnosticsParams()
            {
                Uri = DocumentUri.FromFileSystemPath(path),
                Diagnostics = toSend,
            });
        }
        catch (CompilerException e)
        {
        }
        catch (TemplateCompilationException e)
        {
            if (languageServer == null)
                throw new FileChangedHandlerNotInitialisedException();

            var toSend = new List<Diagnostic>();
            foreach (var error in e.Errors)
            {
                toSend.Add(new Diagnostic()
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = error.ErrorText,
                    Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(error.LineNumber - 1, 0), new Position(error.LineNumber - 1, 1000)),
                    Source = "BitMagic Template Engine",
                });
            }

            filesWithErrors.Add(e.Filename);

            languageServer.SendNotification<PublishDiagnosticsParams>("textDocument/publishDiagnostics", new PublishDiagnosticsParams()
            {
                Uri = DocumentUri.FromFileSystemPath(e.Filename),
                Diagnostics = toSend,
            });
        }
        catch (TemplateException e)
        {
        }
        catch (Exception e)
        {
            // send any errors to the client
            Console.WriteLine(e.Message);
        }
    }

    private sealed class GeneratedFileChangesParameters
    {
        public string[] Filenames { get; set; } = [];
    }

    private string[] ApplyIncrementalChange(string[] originalLines, TextDocumentContentChangeEvent change)
    {
        var range = change.Range!;
        var start = range.Start;
        var end = range.End;

        var before = originalLines.Take(start.Line);
        var after = originalLines.Skip(end.Line + 1);
        string[] affected;

        if (start.Line > originalLines.Length - 1)
            affected = Enumerable.Repeat(string.Empty, end.Line - start.Line + 1).ToArray();
        else if (end.Line > originalLines.Length - 1)
            affected = originalLines[(start.Line)..];
        else
            affected = originalLines[(start.Line)..(end.Line + 1)];

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
            if (original[0].Length <= change.Range!.Start.Character)
            {
                original[0] = original[0].PadRight(change.Range!.Start.Character);
            }
            if (original[^1].Length <= change.Range!.End.Character)
            {
                yield return original[0][..(change.Range!.Start.Character)] +
                             newTextLines[0];
            }
            else
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

public class FileChangedHandlerNotInitialisedException() : Exception("FileChangeHandler Not Initialised, call SetLanguageServer");

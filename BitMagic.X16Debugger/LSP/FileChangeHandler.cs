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
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Server;

namespace BitMagic.X16Debugger.LSP;

internal class FileChangeHandler(DocumentCache documentCache, ProjectBuilder projectBuilder,
    ClientNotificationService clientNotificationService, ServiceManager serviceManager, ProjectService projectService) : IDidChangeTextDocumentHandler
{
    private readonly Debouncer debouncer = new Debouncer();
    private LanguageServer? languageServer = null;
    private HashSet<string> filesWithErrors = new HashSet<string>();
    private bool infraFaultShown;

    public void SetLanguageServer(LanguageServer server)
    {
        languageServer = server;
    }

    public TextDocumentChangeRegistrationOptions GetRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
    {
        return new TextDocumentChangeRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("bmasm"),
            // Full rather than Incremental: if one incremental edit failed to apply, the cached copy
            // would silently drift from the editor and errors would be reported on the wrong lines.
            SyncKind = TextDocumentSyncKind.Full
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

        //Console.WriteLine("-----");
        //foreach (var line in lines)
        //    Console.WriteLine(line);
        //Console.WriteLine("-----");

        // Generated files are a build output (saveGeneratedBmasm), not an input. When one is open
        // the client reports the build rewriting it as a change, which would trigger a second build.
        if (filename.EndsWith(".generated.bmasm", StringComparison.OrdinalIgnoreCase))
            return Unit.Task;

        debouncer.Debounce(UpdateFileChanges);

        return Unit.Task;
    }

    internal void QueueUpdateFileChanges()
    {
        debouncer.Debounce(UpdateFileChanges);
    }

    internal async Task UpdateFileChanges()
    {
        // Coalesce overlapping builds: never run two concurrent projectBuilder.Build()
        // calls against the shared ServiceManager / file managers. This is the same lock a
        // debug launch takes, so an edit during a launch build just retries afterwards.
        var buildGate = serviceManager.BuildLock;
        if (!await buildGate.WaitAsync(0))
        {
            QueueUpdateFileChanges(); // re-arm so the newest edit still builds
            return;
        }

        try
        {
            await UpdateFileChangesCore();
        }
        catch (Exception ex)
        {
            ReportInfrastructureFault(ex);
        }
        finally
        {
            buildGate.Release();
        }
    }

    // A fault in the build pipeline itself (not a user compile error). Surface it to the
    // client so the LSP doesn't just silently stop producing diagnostics.
    private void ReportInfrastructureFault(Exception ex)
    {
        Console.Error.WriteLine($"[X16D] LSP build pipeline fault: {ex}");

        var window = languageServer?.Window;
        if (window == null)
            return;

        window.LogMessage(new LogMessageParams
        {
            Type = MessageType.Error,
            Message = $"BitMagic build pipeline fault: {ex.Message}",
        });

        if (!infraFaultShown)
        {
            infraFaultShown = true;
            window.ShowMessage(new ShowMessageParams
            {
                Type = MessageType.Error,
                Message = "BitMagic build pipeline hit an internal error - see the 'BMASM Language Server' output.",
            });
        }
    }

    private async Task UpdateFileChangesCore()
    {
        if (languageServer == null)
            throw new FileChangedHandlerNotInitialisedException();

        // Every diagnostic this build produced, by file. Published as a whole at the end so the
        // client always reflects the latest build: a file that had errors last time but not this
        // time is cleared, even if this build failed somewhere else.
        var errors = new Dictionary<string, List<Diagnostic>>();

        try
        {
            await projectBuilder.Build();

            clientNotificationService.SendNotfication("bitmagic/generatedchange", new GeneratedFileChangesParameters()
            {
                Filenames = serviceManager.DebugableFileManager.AllFilenames().Where(i => i.EndsWith(".generated.bmasm")).ToArray()
            });
        }
        catch (CompilerLineException e)
        {
            AddSourceError(errors, e.Line.Source.SourceFile, e.Line.Source.LineNumber - 1, e.Message);
        }
        catch (CompilerSourceException e)
        {
            AddSourceError(errors, e.SourceFile.SourceFile, e.SourceFile.LineNumber - 1, e.Message);
        }
        catch (CompilerException e)
        {
            // expected while a file is mid-edit; the specific handlers above already
            // publish diagnostics. Nothing to report here.
        }
        catch (TemplateCompilationException e)
        {
            var toSend = GetErrorList(errors, e.Filename);
            foreach (var error in e.Errors)
            {
                toSend.Add(CreateDiagnostic(error.LineNumber - 1, error.ErrorText));
            }
        }
        catch (TemplateException e)
        {
            // expected while a file is mid-edit; diagnostics are published above.
        }
        catch (Exception e)
        {
            // Usually a malformed file mid-edit tripping the compiler/template engine, not
            // an infrastructure fault - so log it (visible via the stdout drain) but don't
            // raise it to the client as an error. Genuine pipeline faults are caught and
            // reported by the UpdateFileChanges wrapper (ReportInfrastructureFault).
            Console.WriteLine(e.Message);
        }

        foreach (var i in filesWithErrors.Where(i => !errors.ContainsKey(i)))
        {
            PublishDiagnostics(i, []);
        }

        foreach (var (path, diagnostics) in errors)
        {
            PublishDiagnostics(path, diagnostics);
        }

        filesWithErrors = errors.Keys.ToHashSet();
    }

    private void AddSourceError(Dictionary<string, List<Diagnostic>> errors, ISourceFile sourceFile, int lineNumber, string message)
    {
        serviceManager.DebugableFileManager.AddFiles(sourceFile);

        var wrapper = serviceManager.DebugableFileManager.GetWrapper(sourceFile) ?? throw new Exception("Cannot find source file!");

        var ul = wrapper.FindUltimateSource(lineNumber, serviceManager.DebugableFileManager);

        sourceFile = ul.SourceFile ?? sourceFile; // use ultimate source if available

        string path;
        if (sourceFile.Path.EndsWith(".generated.bmasm"))
            path = "bitmagic:generated/" + sourceFile.Path;
        else
            path = sourceFile != null && projectService.Project != null
                ? Path.Combine(projectService.Project.BasePath, sourceFile.Path)
                : "";

        GetErrorList(errors, path).Add(CreateDiagnostic(ul.lineNumber, message));
    }

    private static List<Diagnostic> GetErrorList(Dictionary<string, List<Diagnostic>> errors, string path)
    {
        if (!errors.TryGetValue(path, out var list))
        {
            list = new List<Diagnostic>();
            errors[path] = list;
        }

        return list;
    }

    private static Diagnostic CreateDiagnostic(int lineNumber, string message) => new Diagnostic()
    {
        Severity = DiagnosticSeverity.Error,
        Message = message,
        Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(new Position(lineNumber, 0), new Position(lineNumber, 1000)),
        Source = "BitMagic Template Engine",
    };

    private void PublishDiagnostics(string path, List<Diagnostic> diagnostics)
    {
        languageServer!.SendNotification<PublishDiagnosticsParams>("textDocument/publishDiagnostics", new PublishDiagnosticsParams()
        {
            Uri = DocumentUri.FromFileSystemPath(path),
            Diagnostics = diagnostics,
        });
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

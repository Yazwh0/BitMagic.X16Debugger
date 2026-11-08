using BitMagic.Common;
using BitMagic.Compiler.Exceptions;
using BitMagic.Compiler.Files;
using BitMagic.TemplateEngine.Compiler;
using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.Extensions;
using BitMagic.X16Debugger.LSP;

namespace BitMagic.X16Debugger.Builder;

// Runs ProjectBuilder and turns the compiler/template exceptions it can throw into
// formatted Logger output. Deliberately has no Protocol/Emulator dependency so it can be
// shared between the interactive DAP launch path (DebugBuilder) and the headless
// --buildOnly CLI path (ProjectBuilderFactory.TryBuild) - only the caller knows whether
// there's a DAP session to notify or an emulator to hand off to afterwards.
internal static class ProjectBuildRunner
{
    // Returns false (and has already logged the error) for a compile/template failure.
    // Anything else propagates - the caller decides how to report a genuinely unexpected exception.
    public static bool TryBuild(X16DebugProject debugProject, string workspaceFolder, ServiceManager serviceManager, IEmulatorLogger Logger, out string autobootFile)
    {
        // the error handlers below also touch shared state, so hold the lock for them too
        serviceManager.BuildLock.Wait();
        try
        {
            return TryBuildCore(debugProject, workspaceFolder, serviceManager, Logger, out autobootFile);
        }
        finally
        {
            serviceManager.BuildLock.Release();
        }
    }

    private static bool TryBuildCore(X16DebugProject debugProject, string workspaceFolder, ServiceManager serviceManager, IEmulatorLogger Logger, out string autobootFile)
    {
        autobootFile = "";

        try
        {
            var projectService = new ProjectService();
            projectService.SetProject(debugProject, workspaceFolder);

            var builder = new ProjectBuilder(projectService, serviceManager, Logger);

            builder.Build().GetAwaiter().GetResult();

            autobootFile = debugProject.AutobootFile;

            return true;
        }
        catch (CompilerLineException e)
        {
            var sourceFile = e.Line.Source.SourceFile;
            var lineNumber = e.Line.Source.LineNumber - 1;

            // its very possible the source hasn't been registered, so we need to do it.
            serviceManager.DebugableFileManager.AddFiles(sourceFile);

            var wrapper = serviceManager.DebugableFileManager.GetWrapper(sourceFile) ?? throw new Exception("Cannot find source file!");

            try
            {
                LogSourceError(wrapper, sourceFile, lineNumber, e.Message, workspaceFolder, serviceManager, Logger);
            }
            catch
            {
                Logger.LogLine($"ERROR: \"??\" \"{e.Message}\"");
            }

            return false;
        }
        catch (CompilerSourceException e)
        {
            var sourceFile = e.SourceFile.SourceFile;
            var lineNumber = e.SourceFile.LineNumber - 1;

            serviceManager.DebugableFileManager.AddFiles(sourceFile);

            var wrapper = serviceManager.DebugableFileManager.GetWrapper(sourceFile) ?? throw new Exception("Cannot find source file!");

            LogSourceError(wrapper, sourceFile, lineNumber, e.Message, workspaceFolder, serviceManager, Logger);

            return false;
        }
        catch (CompilerException e)
        {
            Logger.LogLine($"ERROR: {e.Message}");

            return false;
        }
        catch (TemplateCompilationException e)
        {
            Logger.LogLine(""); // ensure there is a new line
            foreach (var error in e.Errors)
            {
                var path = e.Filename != null ? Path.GetRelativePath(workspaceFolder, e.Filename) : "";
                var source = new BitMagicProjectFile(e.Filename);
                if (error.LineNumber >= 0)
                    Logger.LogError($"ERROR: \"{path ?? "??"}\" ({error.LineNumber}) \"{error.ErrorText}\"", source, error.LineNumber);
                else
                    Logger.LogLine($"ERROR: \"{path ?? "??"}\" \"{error.ErrorText}\"");
            }

            return false;
        }
        catch (TemplateException e)
        {
            Logger.LogLine($"ERROR: {e.Message}");

            return false;
        }
    }

    // An error at a line of the compiled code, reported at the file and line it came from, eg the .bmasm line rather
    // than the generated one. Line numbers are 1 based, as in the editor.
    private static void LogSourceError(DebugWrapper wrapper, ISourceFile? sourceFile, int lineNumber, string message, string workspaceFolder,
        ServiceManager serviceManager, IEmulatorLogger Logger)
    {
        var ul = wrapper.FindUltimateSource(lineNumber, serviceManager.DebugableFileManager);
        var file = ul.SourceFile ?? sourceFile;

        // a generated file isn't on disk, so only its name means anything
        var path = file == null ? "??" : file.ActualFile ? Path.GetRelativePath(workspaceFolder, file.Path) : file.Name;

        Logger.LogError($"ERROR: \"{path}\" ({ul.lineNumber + 1}) \"{message}\"", ul.SourceFile, ul.lineNumber + 1);
    }
}

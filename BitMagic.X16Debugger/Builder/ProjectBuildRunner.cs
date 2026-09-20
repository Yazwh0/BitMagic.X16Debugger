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
                var ul = wrapper.FindUltimateSource(lineNumber, serviceManager.DebugableFileManager);

                var path = sourceFile != null ? Path.GetRelativePath(workspaceFolder, sourceFile.Path) : "";

                Logger.LogError($"ERROR: \"{path ?? "??"}\" ({ul.lineNumber}) \"{e.Message}\"", ul.SourceFile, ul.lineNumber + 1);
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

            var ul = wrapper.FindUltimateSource(lineNumber, serviceManager.DebugableFileManager);

            var path = sourceFile != null ? Path.GetRelativePath(workspaceFolder, sourceFile.Path) : "";

            Logger.LogError($"ERROR: \"{path ?? "??"}\" ({ul.lineNumber}) \"{e.Message}\"", ul.SourceFile, ul.lineNumber + 1);

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
}

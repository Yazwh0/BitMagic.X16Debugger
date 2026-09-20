using BitMagic.Common;
using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.LSP;
using BitMagic.X16Emulator;

namespace BitMagic.X16Debugger.Builder;

// Entry point for building a project with no DAP session and no emulator to hand off to
// (the --buildOnly CLI path). Owns a throwaway ServiceManager/Emulator purely because
// ProjectBuilder needs one; nothing here is run.
public static class ProjectBuilderFactory
{
    // Returns false (and has already logged the error) for a compile/template failure.
    // Anything else (bad project JSON, missing files, a genuine bug) propagates as an
    // exception for the caller to report - there's no DAP session to funnel it through.
    public static bool TryBuild(X16DebugProject project, string workspaceFolder, IEmulatorLogger Logger, out string autobootFile)
    {
        var serviceManager = new ServiceManager((o) => new Emulator(), Logger);

        return ProjectBuildRunner.TryBuild(project, workspaceFolder, serviceManager, Logger, out autobootFile);
    }
}

internal class ProjectBuilder(ProjectService projectService, ServiceManager serviceManager, IEmulatorLogger Logger)
{
    public async Task Build()
    {
        var project = projectService.Project ?? throw new Exception("No project set");

        if (project.Files != null)
        {
            foreach (var i in project.Files)
            {
                if (i is BitmagicInputFile bitmagicFile)
                {
                    var (result, state) = await serviceManager.BitmagicBuilder.Build(bitmagicFile.Filename, project.BasePath, project.CompileOptions);
                    if (result != null)
                    {
                        serviceManager.ExpressionManager.SetState(state);

                        var prg = result.Source as IBinaryFile ?? throw new Exception("result is not a IBinaryFile!");

                        if (project.AutobootRun && string.IsNullOrWhiteSpace(project.AutobootFile))
                        {
                            project.AutobootFile = prg.Name;
                        }
                    }
                }
                else if (i is Cc65InputFile cc65File)
                {
                    Cc65BinaryFileFactory.BuildAndAdd(cc65File, serviceManager, project.BasePath, Logger);
                }

                // write files after each step incase there is a pre-requisite.
                WriteOutputFiles(project);
            }
        }

        if (!string.IsNullOrWhiteSpace(project.Source))
        {
            var (result, state) = await serviceManager.BitmagicBuilder.Build(project.Source, project.BasePath, project.CompileOptions);
            if (result != null)
            {
                serviceManager.ExpressionManager.SetState(state);

                var prg = result.Source as IBinaryFile ?? throw new Exception("result is not a IBinaryFile!");

                if (project.AutobootRun && string.IsNullOrWhiteSpace(project.AutobootFile))
                {
                    project.AutobootFile = prg.Name;
                }
            }
            else
            {
                Logger.LogLine("Build didn't result in a result.");
            }

            WriteOutputFiles(project);
        }
    }

    private void WriteOutputFiles(X16DebugProject project)
    {
        if (string.IsNullOrWhiteSpace(project.OutputFolder))
            return;

        foreach (var f in serviceManager.DebugableFileManager.GetBitMagicFilesToWrite().Where(i => !i.Written))
        {
            string path = "";
            if (Path.IsPathRooted(project.OutputFolder))
            {
                path = Path.GetFullPath(Path.Combine(project.OutputFolder, f.Path));
            }
            else
            {
                path = Path.GetFullPath(Path.Combine(projectService.WorkspaceFolder ?? "", project.OutputFolder, f.Path));
            }

            Logger.Log($"Writing to '{path}'... ");
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(path, f.Data.ToArray());
            Logger.LogLine("Done.");
            f.SetWritten();
        }
    }
}


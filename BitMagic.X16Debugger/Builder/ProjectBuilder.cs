using BitMagic.Common;
using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.LSP;

namespace BitMagic.X16Debugger.Builder;

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
                if (!string.IsNullOrWhiteSpace(project.OutputFolder))
                {
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
                        File.WriteAllBytes(path, f.Data.ToArray());
                        Logger.LogLine("Done.");
                        f.SetWritten();
                   }
                }
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
        }
    }
}


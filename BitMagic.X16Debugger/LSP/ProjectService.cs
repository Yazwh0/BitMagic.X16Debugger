using BitMagic.X16Debugger.Builder;

namespace BitMagic.X16Debugger.LSP;

internal class ProjectService
{
    public X16DebugProject? Project { get; internal set; }
    public string? WorkspaceFolder { get; internal set; }

    public void SetProject(X16DebugProject? project, string workspaceFolder)
    {
        Project = project;
        WorkspaceFolder = workspaceFolder;
    }
}

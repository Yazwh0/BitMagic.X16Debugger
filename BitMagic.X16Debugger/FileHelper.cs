namespace BitMagic.X16Debugger;

internal static class FileHelper
{
    public static string? GetFilename(string filename, X16DebugProject project, string? workspace)
    {
        if (File.Exists(filename)) 
            return filename;

        var baseFilename = Path.GetFullPath(Path.Combine(project.BasePath, filename));

        if (File.Exists(baseFilename))
            return baseFilename;

        if (string.IsNullOrEmpty(workspace)) return null;

        var workspaceFilename = Path.GetFullPath(Path.Combine(workspace, filename));

        if (File.Exists(workspaceFilename))
            return workspaceFilename;

        return null;
    }
}

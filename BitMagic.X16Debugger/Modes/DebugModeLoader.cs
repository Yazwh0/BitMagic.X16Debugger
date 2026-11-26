using System.Reflection;
using System.Runtime.Loader;
using BitMagic.Common;

namespace BitMagic.X16Debugger.Modes;

internal static class DebugModeLoader
{
    private static bool _resolving;

    /// <summary>
    /// A mode references the BitMagic assemblies it was built against, at their version then. X16D's own can have
    /// a lower number (a develop build is newer code than the last release, but numbered before it), which .NET
    /// won't accept. So when a mode asks for an assembly X16D already has, it gets X16D's, whatever the version:
    /// that's the one it has to use anyway, and <see cref="IX16DebugMode.ApiVersion"/> is the check that matters.
    /// </summary>
    private static void ShareLoadedAssemblies()
    {
        if (_resolving)
            return;

        _resolving = true;
        AssemblyLoadContext.Default.Resolving += (_, wanted) =>
            AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, wanted.Name, StringComparison.OrdinalIgnoreCase));
    }

    public static IX16DebugMode Load(DebugModeSettings settings, string basePath, IEmulatorLogger logger)
    {
        ShareLoadedAssemblies();
        var path = FindAssembly(settings, basePath, logger);

        // LoadFrom so the mode's own dependencies are found next to it, while it shares X16D's copy of this assembly
        var assembly = Assembly.LoadFrom(path);

        var types = assembly.GetTypes()
            .Where(t => typeof(IX16DebugMode).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .ToList();

        if (!string.IsNullOrWhiteSpace(settings.Type))
            types = types.Where(t => t.FullName == settings.Type || t.Name == settings.Type).ToList();

        var mode = types.Count switch
        {
            0 => throw new Exception($"'{path}' has no debug mode{(settings.Type == null ? "" : $" called '{settings.Type}'")}."),
            > 1 => throw new Exception($"'{path}' has more than one debug mode, so 'type' must say which: {string.Join(", ", types.Select(t => t.FullName))}."),
            _ => (IX16DebugMode)(Activator.CreateInstance(types[0]) ?? throw new Exception($"Could not create '{types[0].FullName}'."))
        };

        if (mode.ApiVersion != DebugModeApi.Version)
            throw new Exception($"'{mode.Name}' was built for version {mode.ApiVersion} of X16D's debug modes, but this X16D has version {DebugModeApi.Version}. " +
                (mode.ApiVersion < DebugModeApi.Version ? "Use a newer version of the debug mode." : "Update BitMagic."));

        return mode;
    }

    /// <summary>
    /// The mode's assembly: from a NuGet package if <see cref="DebugModeSettings.Package"/> is set (where
    /// <see cref="DebugModeSettings.Assembly"/>, if given, names the file in it), otherwise the path in
    /// <see cref="DebugModeSettings.Assembly"/>.
    /// </summary>
    private static string FindAssembly(DebugModeSettings settings, string basePath, IEmulatorLogger logger)
    {
        if (!string.IsNullOrWhiteSpace(settings.Package))
        {
            var folder = DebugModePackages.Fetch(settings.Package, settings.Version, settings.Source, logger);
            var file = Path.Combine(folder, string.IsNullOrWhiteSpace(settings.Assembly) ? settings.Package + ".dll" : Path.GetFileName(settings.Assembly));
            return File.Exists(file) ? file : throw new Exception($"The {settings.Package} package doesn't have '{Path.GetFileName(file)}'.");
        }

        if (string.IsNullOrWhiteSpace(settings.Assembly))
            throw new Exception("debugMode needs a 'package' or an 'assembly'.");

        var path = Path.IsPathRooted(settings.Assembly) ? settings.Assembly : Path.GetFullPath(settings.Assembly, basePath);
        return File.Exists(path) ? path : throw new Exception($"Debug mode assembly '{path}' not found.");
    }
}

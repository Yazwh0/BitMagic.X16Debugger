using System.Reflection;
using BitMagic.Common;

namespace BitMagic.X16Debugger.Modes;

internal static class DebugModeLoader
{
    public static IX16DebugMode Load(DebugModeSettings settings, string basePath, IEmulatorLogger logger)
    {
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

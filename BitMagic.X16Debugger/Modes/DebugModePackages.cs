using System.Reflection;
using System.Runtime.Versioning;
using BitMagic.Common;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace BitMagic.X16Debugger.Modes;

/// <summary>
/// Gets a debug mode from a NuGet package: downloads it, and any dependencies X16D doesn't already have,
/// into a cache, and returns the folder they're in. A version that's already in the cache isn't downloaded
/// again, and if the source can't be reached the newest cached version that fits is used.
/// </summary>
internal static class DebugModePackages
{
    public const string DefaultSource = "https://api.nuget.org/v3/index.json";

    /// <summary>Where packages are kept: BITMAGIC_DEBUGMODE_CACHE, or BitMagic/DebugModes in local app data.</summary>
    public static string CacheFolder =>
        Environment.GetEnvironmentVariable("BITMAGIC_DEBUGMODE_CACHE") is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BitMagic", "DebugModes");

    private const string CompleteMarker = ".complete";

    /// <param name="version">An exact version, a range or float (eg "0.2.*"), or empty for the latest release.</param>
    public static string Fetch(string packageId, string? version, string? source, IEmulatorLogger logger)
    {
        var packageFolder = Path.Combine(CacheFolder, packageId.ToLowerInvariant());
        var cached = Directory.Exists(packageFolder)
            ? Directory.GetDirectories(packageFolder)
                .Where(d => File.Exists(Path.Combine(d, CompleteMarker)))
                .Select(d => NuGetVersion.TryParse(Path.GetFileName(d), out var v) ? v : null)
                .OfType<NuGetVersion>()
                .ToList()
            : [];

        // an exact version that's cached needs nothing from the source
        if (NuGetVersion.TryParse(version, out var exact) && cached.Contains(exact))
            return Path.Combine(packageFolder, exact.ToNormalizedString());

        var log = new Logger(logger);
        var cache = new SourceCacheContext();
        var repository = Repository.Factory.GetCoreV3(source ?? DefaultSource);

        NuGetVersion? chosen = null;
        FindPackageByIdResource? resource = null;
        try
        {
            resource = repository.GetResourceAsync<FindPackageByIdResource>().GetAwaiter().GetResult();
            var available = resource.GetAllVersionsAsync(packageId, cache, log, CancellationToken.None).GetAwaiter().GetResult().ToList();
            chosen = Choose(available, version);
            if (chosen == null)
                throw new Exception($"'{source ?? DefaultSource}' has no version of {packageId}{(string.IsNullOrWhiteSpace(version) ? "" : $" matching {version}")}.");
        }
        catch (Exception e) when (cached.Count > 0)
        {
            chosen = Choose(cached, version) ?? throw new Exception($"Couldn't get {packageId} ({e.Message}), and no cached version matches {version}.");
            logger.LogLine($"Couldn't check for {packageId} ({e.Message}), so using version {chosen} from the cache.");
            return Path.Combine(packageFolder, chosen.ToNormalizedString());
        }

        var folder = Path.Combine(packageFolder, chosen.ToNormalizedString());
        if (File.Exists(Path.Combine(folder, CompleteMarker)))
            return folder;

        logger.LogLine($"Downloading {packageId} {chosen}...");
        if (Directory.Exists(folder))
            Directory.Delete(folder, true); // a download that didn't finish
        Directory.CreateDirectory(folder);

        Download(resource!, new PackageIdentity(packageId, chosen), folder, cache, log, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        File.WriteAllText(Path.Combine(folder, CompleteMarker), DateTime.UtcNow.ToString("O"));
        return folder;
    }

    private static NuGetVersion? Choose(IReadOnlyCollection<NuGetVersion> versions, string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return versions.Where(v => !v.IsPrerelease).Max() ?? versions.Max();

        if (NuGetVersion.TryParse(version, out var exact))
            return versions.Contains(exact) ? exact : null;

        return VersionRange.TryParse(version, true, out var range) ? range.FindBestMatch(versions) : null;
    }

    private static void Download(FindPackageByIdResource resource, PackageIdentity package, string folder, SourceCacheContext cache, ILogger log, HashSet<string> done)
    {
        if (!done.Add(package.Id))
            return;

        using var stream = new MemoryStream();
        if (!resource.CopyNupkgToStreamAsync(package.Id, package.Version, stream, cache, log, CancellationToken.None).GetAwaiter().GetResult())
            throw new Exception($"Couldn't download {package}.");

        stream.Position = 0;
        using var reader = new PackageArchiveReader(stream);

        var current = CurrentFramework();
        var reducer = new FrameworkReducer();

        var libs = reader.GetLibItems().ToList();
        var nearest = reducer.GetNearest(current, libs.Select(l => l.TargetFramework));
        if (nearest == null)
            throw new Exception($"{package} has nothing for {current.GetShortFolderName()}.");

        foreach (var file in libs.Single(l => l.TargetFramework == nearest).Items)
        {
            var target = Path.Combine(folder, Path.GetFileName(file));
            using var entry = reader.GetStream(file);
            using var output = File.Create(target);
            entry.CopyTo(output);
        }

        var dependencyGroups = reader.GetPackageDependencies().ToList();
        var dependencyFramework = reducer.GetNearest(current, dependencyGroups.Select(g => g.TargetFramework));
        var dependencies = dependencyGroups.FirstOrDefault(g => g.TargetFramework == dependencyFramework)?.Packages ?? [];

        foreach (var dependency in dependencies)
        {
            if (AlreadyHave(dependency.Id))
                continue;

            var versions = resource.GetAllVersionsAsync(dependency.Id, cache, log, CancellationToken.None).GetAwaiter().GetResult();
            var best = dependency.VersionRange.FindBestMatch(versions) ?? throw new Exception($"No version of {dependency.Id} matches {dependency.VersionRange}, needed by {package}.");
            Download(resource, new PackageIdentity(dependency.Id, best), folder, cache, log, done);
        }
    }

    /// <summary>X16D already has it: loaded, or next to X16D. Using X16D's copy keeps the types the same.</summary>
    private static bool AlreadyHave(string id) =>
        AppDomain.CurrentDomain.GetAssemblies().Any(a => string.Equals(a.GetName().Name, id, StringComparison.OrdinalIgnoreCase)) ||
        File.Exists(Path.Combine(AppContext.BaseDirectory, id + ".dll"));

    private static NuGetFramework CurrentFramework()
    {
        var name = Assembly.GetEntryAssembly()?.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;
        return name == null ? NuGetFramework.Parse("net10.0") : NuGetFramework.Parse(name);
    }

    private sealed class Logger(IEmulatorLogger logger) : LoggerBase
    {
        public override void Log(ILogMessage message)
        {
            if (message.Level is LogLevel.Error or LogLevel.Warning)
                logger.LogLine($"NuGet: {message.Message}");
        }

        public override Task LogAsync(ILogMessage message)
        {
            Log(message);
            return Task.CompletedTask;
        }
    }
}

using BitMagic.Common;
using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Emulator;

namespace BitMagic.X16Debugger;

/// <summary>
/// Puts everything on the SD card through one process. The project's sdCardFiles go where they say, and any built X16
/// file they don't place goes to the path it was built to (the implied output). AUTOBOOT.X16 then loads the autoboot
/// file from wherever it ended up. As a built file is placed its SD card location is recorded, so a LOAD of it finds
/// its debugger info.
/// </summary>
internal class SdCardWriter(SdCard sdCard, DebugableFileManager files, IEmulatorLogger logger)
{
    private record Placement(string SdCardPath, Func<byte[]> GetData, bool AllowOverwrite, DebugWrapper? File);

    /// <param name="excludes">Built files not to put on the card, eg those patched into ROM.</param>
    /// <param name="autobootFile">The file AUTOBOOT.X16 loads, by the path it was built to. Empty for none.</param>
    public void Write(IEnumerable<SdCardFile> sdCardFiles, string basePath, ICollection<string> excludes, string autobootFile)
    {
        var projectPlacements = new List<Placement>();

        foreach (var entry in sdCardFiles)
        {
            foreach (var hostFile in GetHostFiles(entry, basePath))
            {
                // a built file is written from the build, the same as when it's implied
                var file = files.GetFileByHostFile(hostFile);
                var getData = file != null ? () => GetData(file) : (Func<byte[]>)(() => File.ReadAllBytes(hostFile));

                projectPlacements.Add(new Placement(SdCard.GetSdCardPath(hostFile, entry.Dest), getData, entry.AllowOverwrite, file));
            }
        }

        var placedByProject = projectPlacements.Where(i => i.File != null).Select(i => i.File!).ToHashSet();

        var placements = files.X16Files()
            .Where(i => !placedByProject.Contains(i) && !excludes.Contains(i.Path))
            .Select(i => new Placement(SdCard.GetSdCardPath(i.Path), () => GetData(i), true, i))
            .Concat(projectPlacements);

        foreach (var placement in placements)
        {
            var written = sdCard.WriteFile(placement.SdCardPath, placement.GetData(), placement.AllowOverwrite);

            if (written != null && placement.File != null)
                placement.File.PlacedOnSdCard(X16Path.FromSdCardPath(written));
        }

        foreach (var file in placedByProject)
            logger.LogLine($"Debugger info for '{file.Path}' is at '{string.Join("', '", file.SdCardPaths)}' on the SD card.");

        WriteAutoboot(autobootFile);
    }

    private void WriteAutoboot(string autobootFile)
    {
        if (string.IsNullOrWhiteSpace(autobootFile))
            return;

        // load it from where it was placed, which isn't where it was built to if the project moved it
        var placed = files.GetFile_New(autobootFile)?.SdCardPaths.FirstOrDefault();
        var target = placed != null ? placed.TrimStart('/') : autobootFile;

        logger.Log($"Adding AUTOBOOT.X16 for '{target}'... ");

        if (sdCard.FileSystem.Exists("AUTOBOOT.X16"))
        {
            logger.LogLine("Error. File already exists.");
            return;
        }

        sdCard.WriteFile("AUTOBOOT.X16", AutobootCreator.GetAutoboot(target), false);
        logger.LogLine("Done.");
    }

    private static byte[] GetData(DebugWrapper file) => ((IBinaryFile)file.Source).Data.ToArray();

    // The host files an sdCardFiles entry names: a file, a directory's files, or a wildcard.
    private IEnumerable<string> GetHostFiles(SdCardFile entry, string basePath)
    {
        var name = Path.GetFullPath(entry.Source, basePath);

        if (File.Exists(name))
            return [name];

        if (System.IO.Directory.Exists(name))
            return System.IO.Directory.GetFiles(name, "*.*");

        var path = Path.GetDirectoryName(name);
        if (!System.IO.Directory.Exists(path))
        {
            logger.LogError($"Cannot find directory: {path}");
            return [];
        }

        return System.IO.Directory.GetFiles(path, Path.GetFileName(name));
    }
}

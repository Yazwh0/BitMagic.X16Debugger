using BitMagic.Common;
using BitMagic.DiscUtils.Fat;
using BitMagic.X16Emulator;
using FatDirectory = BitMagic.DiscUtils.Fat.Directory;

namespace BitMagic.X16Debugger;

/// <summary>
/// Tracks the X16 DOS current directory, so the debugger can work out which file on the SD card a SETNAM refers to.
/// The DOS keeps the cluster of the current directory in fat32's cur_volume, in RAM bank 0. The cluster is mapped to
/// a path by walking the directories on the SD card.
/// </summary>
internal class X16DirectoryTracker
{
    // Offsets into fat32's 'struct fs', see fat32/fat32.s in the x16-rom.
    private const int FsMounted = 0;
    private const int FsRootDirCluster = 1;
    private const int FsCwdCluster = 39;
    private const int FsSize = 43;

    // cur_volume in the R4x ROMs. Used when the fat32 symbols aren't available.
    internal const int DefaultCurVolumeAddress = 0xb800;
    private const string CurVolumeSymbol = ".cur_volume";
    private const int MaxDepth = 32;

    private readonly Emulator _emulator;
    private readonly IEmulatorLogger _logger;
    private readonly Dictionary<uint, string> _directories = new();
    private int _curVolumeAddress = DefaultCurVolumeAddress;
    private bool _invalidVolumeLogged;
    private uint _missingClusterLogged;

    public X16DirectoryTracker(Emulator emulator, IEmulatorLogger logger)
    {
        _emulator = emulator;
        _logger = logger;
    }

    /// <summary>
    /// The current directory as of the last Refresh, eg '/GAME'.
    /// </summary>
    public string CurrentDirectory { get; private set; } = X16Path.Root;

    /// <summary>
    /// Looks for cur_volume in the ROM bank symbol files, as its address depends on the ROM version.
    /// </summary>
    public void FindCurVolume(IEnumerable<SymbolsFile> symbolsFiles)
    {
        foreach (var file in symbolsFiles.Where(i => i.RomBank != null && File.Exists(i.Symbols)))
        {
            foreach (var line in File.ReadLines(file.Symbols))
            {
                // eg 'al 00B800 .cur_volume'
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length < 3 || parts[2] != CurVolumeSymbol)
                    continue;

                var address = Convert.ToInt32(parts[1], 16);
                if (address < 0xa000 || address + FsSize > 0xc000)
                {
                    _logger.LogLine($"Warning: {CurVolumeSymbol[1..]} in '{file.Symbols}' is at ${address:X4}, which isn't in banked RAM. Using ${_curVolumeAddress:X4}.");
                    return;
                }

                _curVolumeAddress = address;
                return;
            }
        }
    }

    /// <summary>
    /// Address of fat32's cur_volume in RAM bank 0.
    /// </summary>
    public int CurVolumeAddress => _curVolumeAddress;

    /// <summary>
    /// The fields of fat32's cur_volume used to find the current directory.
    /// </summary>
    public (bool Mounted, uint RootCluster, uint CwdCluster) ReadVolume()
    {
        var fs = _emulator.RamBank.Slice(_curVolumeAddress - 0xa000, FsSize);

        return (fs[FsMounted] != 0, BitConverter.ToUInt32(fs[FsRootDirCluster..]), BitConverter.ToUInt32(fs[FsCwdCluster..]));
    }

    /// <summary>
    /// Reads the current directory from the DOS.
    /// </summary>
    public string Refresh()
    {
        CurrentDirectory = ReadCurrentDirectory();
        return CurrentDirectory;
    }

    /// <summary>
    /// Resolves a SETNAM value against the DOS current directory, eg 'LEVEL1.PRG' gives '/GAME/LEVEL1.PRG'.
    /// </summary>
    public string Resolve(string dosName) => X16Path.ResolveDosName(Refresh(), dosName);

    private string ReadCurrentDirectory()
    {
        var sdCard = _emulator.SdCard;
        if (sdCard == null)
            return X16Path.Root;

        var (mounted, rootCluster, cwdCluster) = ReadVolume();

        // not mounted yet, so the DOS will start at the root
        if (!mounted)
            return X16Path.Root;

        if (rootCluster != sdCard.FileSystem.RootDirectoryCluster)
        {
            // most likely a ROM where cur_volume is elsewhere and there are no symbols to find it
            if (!_invalidVolumeLogged)
                _logger.LogLine($"Warning: Cannot find the DOS current directory at ${_curVolumeAddress:X4}, filenames will be relative to the root of the SD card.");

            _invalidVolumeLogged = true;
            return X16Path.Root;
        }

        if (cwdCluster == 0 || cwdCluster == rootCluster)
            return X16Path.Root;

        var path = FindDirectory(sdCard.FileSystem, cwdCluster);
        if (path != null)
            return path;

        if (_missingClusterLogged != cwdCluster)
            _logger.LogLine($"Warning: Cannot find the DOS current directory (cluster {cwdCluster}) on the SD card, filenames will be relative to the root.");

        _missingClusterLogged = cwdCluster;
        return X16Path.Root;
    }

    private string? FindDirectory(FatFileSystem fileSystem, uint cluster)
    {
        if (_directories.TryGetValue(cluster, out var path))
            return path;

        // the X16 may have created directories since we last looked, so reread them from the image
        _directories.Clear();
        fileSystem.UpdateCaches();
        AddDirectories(fileSystem.RootDir, "", 0);

        return _directories.GetValueOrDefault(cluster);
    }

    private void AddDirectories(FatDirectory directory, string path, int depth)
    {
        if (depth > MaxDepth)
            return;

        foreach (var entry in directory.GetDirectories().ToArray())
        {
            // also stops any loops in a corrupt image
            if (entry.FirstCluster == 0 || _directories.ContainsKey(entry.FirstCluster))
                continue;

            var childPath = $"{path}/{entry.Name}";
            _directories.Add(entry.FirstCluster, childPath);

            var child = directory.GetChildDirectory(entry.Name);
            if (child == null)
                continue;

            child.LoadEntries();
            AddDirectories(child, childPath, depth + 1);
        }
    }
}

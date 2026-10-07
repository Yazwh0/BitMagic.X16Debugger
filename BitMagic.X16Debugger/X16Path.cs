namespace BitMagic.X16Debugger;

/// <summary>
/// CMDR-DOS filename handling, following dos/parser.s in the x16-rom.
/// A name is '[@][medium][/path/]:name[,options]' or just 'name'. The path and name are joined into a unix style path,
/// which the DOS then resolves against its current directory.
/// Paths returned here are absolute from the root of the SD card and use '/', eg '/GAME/LEVEL1.PRG'.
/// </summary>
internal static class X16Path
{
    public const string Root = "/";

    /// <summary>
    /// The unix style path the DOS builds from a SETNAM value, eg '//GAME/:LEVEL1.PRG,P' gives '/GAME/LEVEL1.PRG'.
    /// </summary>
    public static string ToUnixPath(string dosName)
    {
        var path = "";
        var name = dosName;

        var colon = dosName.IndexOf(':');
        if (colon != -1)
        {
            path = dosName[..colon];
            name = dosName[(colon + 1)..];

            if (path.StartsWith('@'))
                path = path[1..];

            // medium (partition) number, only partition 0 is supported
            path = path.TrimStart("0123456789".ToCharArray());

            // the path must start and end with '/'. The first '/' is the delimiter, so '//DIR/' is absolute and
            // '/DIR/' is relative. Anything else is ignored by the DOS.
            if (path.Length >= 2 && path[0] == '/' && path[^1] == '/')
                path = path[1..];
            else
                path = "";
        }

        var options = name.IndexOf(',');
        if (options != -1)
            name = name[..options];

        return path + name;
    }

    /// <summary>
    /// Resolves a unix style path against the current directory, folding '.' and '..'.
    /// </summary>
    public static string Resolve(string currentDirectory, string unixPath)
    {
        var parts = new List<string>();

        if (!unixPath.StartsWith('/'))
            parts.AddRange(Split(currentDirectory));

        foreach (var part in Split(unixPath))
        {
            if (part == ".")
                continue;

            if (part == "..")
            {
                if (parts.Count > 0)
                    parts.RemoveAt(parts.Count - 1);
                continue;
            }

            parts.Add(part);
        }

        return Root + string.Join('/', parts);
    }

    /// <summary>
    /// Resolves a SETNAM value against the current directory.
    /// </summary>
    public static string ResolveDosName(string currentDirectory, string dosName) =>
        Resolve(currentDirectory, ToUnixPath(dosName));

    /// <summary>
    /// The filename part of a path, eg 'LEVEL1.PRG' from '/GAME/LEVEL1.PRG'.
    /// </summary>
    public static string GetFilename(string path) => path[(path.LastIndexOf('/') + 1)..];

    /// <summary>
    /// The path in the form the SD card's FatFileSystem expects, eg 'GAME/LEVEL1.PRG'. The root is ''.
    /// </summary>
    public static string ToSdCardPath(string path) => path.TrimStart('/');

    /// <summary>
    /// An SD card path, which uses either separator, as an X16 path. Eg 'GAME\LEVEL1.PRG' gives '/GAME/LEVEL1.PRG'.
    /// </summary>
    public static string FromSdCardPath(string sdCardPath) => Resolve(Root, sdCardPath.Replace('\\', '/'));

    private static string[] Split(string path) => path.Split('/', StringSplitOptions.RemoveEmptyEntries);
}

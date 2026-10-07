namespace BitMagic.X16Debugger;

/// <summary>
/// The kernal file calls seen by the debugger, so a LOAD can be matched to the file named by the SETNAM before it.
/// </summary>
internal class KernalFileState
{
    /// <summary>
    /// The filename as passed to SETNAM.
    /// </summary>
    public string SetNam { get; set; } = "";

    /// <summary>
    /// The SETNAM filename resolved against the DOS current directory, eg '/GAME/LEVEL1.PRG'.
    /// </summary>
    public string Path { get; set; } = "";

    public bool FileExists { get; set; }

    /// <summary>
    /// Under 2 bytes, so there is no header and nothing to load.
    /// </summary>
    public bool FileTooShort { get; set; }

    /// <summary>
    /// The load address from the file's header.
    /// </summary>
    public int HeaderAddress { get; set; }

    public int LogicalFile { get; set; }
    public int Device { get; set; }
    public int SecondaryAddress { get; set; }
}

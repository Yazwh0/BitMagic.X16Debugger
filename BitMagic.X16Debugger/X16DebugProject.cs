using Newtonsoft.Json;
using System.Text;

namespace BitMagic.X16Debugger;

public class X16DebugProject
{
    /// <summary>
    /// Start the application in stepping mode.
    /// </summary>
    [JsonProperty("startStepping", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool StartStepping { get; set; } = true;

    /// <summary>
    /// Main source file.
    /// </summary>
    [JsonProperty("source", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Source { get; set; } = "";

    /// <summary>
    /// Compile the source and add it as a file.
    /// </summary>
    public bool RunSource { get; set; } = false;

    /// <summary>
    /// Location to save the .prg from the source file on the host. (Not on the sdcard.)
    /// </summary>
    public string SourcePrg { get; set; } = "";

    /// <summary>
    /// Start address. If omitted or -1, will start the ROM normally from the vector at $fffc.
    /// </summary>
    [JsonProperty("startAddress", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int StartAddress { get; set; } = -1;

    /// <summary>
    /// ROM file to use.
    /// </summary>
    [JsonProperty("romFile", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string RomFile { get; set; } = "";

    /// <summary>
    /// List of files that can be imported for symbols.
    /// </summary>
    [JsonProperty("symbols", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public SymbolsFile[] Symbols { get; set; } = Array.Empty<SymbolsFile>();

    /// <summary>
    /// Show DAP messages between calling host and debugger.
    /// </summary>
    [JsonProperty("showDAPMessage", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool ShowDAPMessages { get; set; } = false;

    /// <summary>
    /// Display names for the Rom banks.
    /// </summary>
    public string[] RomBankNames { get; set; } = new string[] { "Kernel", "Keyboard", "Dos", "Geos", "Basic", "Monitor", "Charset", "Codex", "Graph", "Demo", "Audio" };

    /// <summary>
    /// Display names for the Ram banks.
    /// </summary>
    public string[] RamBankNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Machine to load globals from if there is no bmasm source.
    /// </summary>
    public string Machine { get; set; } = "";

    /// <summary>
    /// Prefill the keyboard buffer with this data. 16bytes max, rest are discarded.
    /// </summary>
    public byte[] KeyboardBuffer { get; set; } = new byte[] { };

    /// <summary>
    /// Prefill the mouse buffer with this data. 8bytes max, rest are discarded.
    /// </summary>
    public byte[] MouseBuffer { get; set; } = new byte[] { };

    /// <summary>
    /// RTC NvRam Data
    /// </summary>
    public RtcNvram NvRam { get; set; } = new RtcNvram();

    /// <summary>
    /// Files to add to the root directory of the SDCard. Wildcards accepted.
    /// </summary>
    public string[] SdCardFiles = new string[] { };
}

public class RtcNvram
{
    /// <summary>
    /// Filename to load into 0x00 -> 0x60 in the RTCs NVRAM.
    /// Not used if Data has values.
    /// </summary>
    public string File { get; set; } = "";

    /// <summary>
    /// Data to load into 0x00 -> 0x60 in the RTCs NVRAM.
    /// </summary>
    public byte[] Data { get; set; } = new byte[] { };

    /// <summary>
    /// Filename to store the RTCs NVRAM in. This will overwrite.
    /// </summary>
    public string WriteFile { get; set; } = "";
}

public class SymbolsFile
{
    /// <summary>
    /// File name.
    /// </summary>
    [JsonProperty("name", DefaultValueHandling = DefaultValueHandling.Ignore, Required = Required.Always)]
    public string Name { get; set; } = "";

    /// <summary>
    /// ROM bank that the symbols are for. Omit or not a rombank file. Any symbols in the ROM area will be discarded.
    /// </summary>
    [JsonProperty("romBank", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int? RomBank { get; set; }

    /// <summary>
    /// RAM bank that the symbols are for. Omit for not a rambank file. Any symbols in the RAM area will be discarded.
    /// </summary>
    [JsonProperty("ramBank", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int? RamBank { get; set; }

    /// <summary>
    /// Range of memory that is a jump table. Used to create extra symbols.
    /// </summary>
    public RangeDefinition[] RangeDefinitions { get; set; } = Array.Empty<RangeDefinition>();
}

public class RangeDefinition
{
    /// <summary>
    /// Start address of the jump table
    /// </summary>
    public string Start { get; set; } = "";

    /// <summary>
    /// End address of the jump table
    /// </summary>
    public string End { get; set; } = "";

    /// <summary>
    /// Type of definition, supported : 'jumptable'
    /// </summary>
    public string Type { get; set; } = "jumptable";
}
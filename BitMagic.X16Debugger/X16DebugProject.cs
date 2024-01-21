using BitMagic.Compiler;
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
    /// Directly run the compiled code, or if false compile the source and add it as a file to the SDCard.
    /// </summary>
    public bool RunSource { get; set; } = false;

    /// <summary>
    /// Location to save the .prg and other files from the source file on the host. (Not on the sdcard.)
    /// </summary>
    public string OutputFolder { get; set; } = "";

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
    /// Display names for the Rom banks.
    /// </summary>
    [JsonProperty("romBankNames", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string[] RomBankNames { get; set; } = new string[] { "Kernel", "Keyboard", "Dos", "Geos", "Basic", "Monitor", "Charset", "Codex", "Graph", "Demo", "Audio", "Util", "Bannex" };

    /// <summary>
    /// Display names for the Ram banks.
    /// </summary>
    [JsonProperty("ramBankNames", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string[] RamBankNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Machine to load globals from if there is no bmasm source.
    /// </summary>
    [JsonProperty("machine", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Machine { get; set; } = "";

    /// <summary>
    /// Prefill the keyboard buffer with this data. 16bytes max, rest are discarded.
    /// </summary>
    [JsonProperty("keyboardBuffer", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public byte[] KeyboardBuffer { get; set; } = new byte[] { };

    /// <summary>
    /// Prefill the mouse buffer with this data. 8bytes max, rest are discarded.
    /// </summary>
    [JsonProperty("mouseBuffer", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public byte[] MouseBuffer { get; set; } = new byte[] { };

    /// <summary>
    /// RTC NvRam Data
    /// </summary>
    [JsonProperty("nvRam", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public RtcNvram NvRam { get; set; } = new RtcNvram();

    /// <summary>
    /// Files to add to the root directory of the SDCard. Wildcards accepted.
    /// </summary>
    [JsonProperty("sdCardFiles", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string[] SdCardFiles = new string[] { };

    /// <summary>
    /// Capture changes between every time the emulator is paused. (Eg breakpoints or stepping)
    /// </summary>
    [JsonProperty("captureChanges", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool CaptureChanges { get; set; } = false;

    /// <summary>
    /// Cartridge file to load
    /// </summary>
    [JsonProperty("cartridge", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Cartridge { get; set; } = "";

    /// <summary>
    /// Display Segments
    /// </summary>
    [JsonProperty("compileOptions", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public CompileOptions? CompileOptions { get; set; } = null;

    /// <summary>
    /// Value to fill CPU RAM and VRAM with at startup.
    /// </summary>
    [JsonProperty("memoryFillValue", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public byte MemoryFillValue { get; set; } = 0;
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
    [JsonProperty("symbols", DefaultValueHandling = DefaultValueHandling.Ignore, Required = Required.Always)]
    public string Symbols { get; set; } = "";

    /// <summary>
    /// ROM bank that the symbols are for. Omit if not a rombank file. Any symbols in the ROM area will be discarded.
    /// </summary>
    [JsonProperty("romBank", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int? RomBank { get; set; } = null;

    /// <summary>
    /// RAM bank that the symbols are for. Omit if not a rambank file. Any symbols in the RAM area will be discarded.
    /// </summary>
    [JsonProperty("ramBank", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public int? RamBank { get; set; } = null;

    /// <summary>
    /// X16 Filename that the symbols are for. Omit if not a X16 binary.
    /// </summary>
    [JsonProperty("filename", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public string Filename { get; set; } = "";

    /// <summary>
    /// Range of memory that is a jump table. Used to create extra symbols.
    /// </summary>
    [JsonProperty("rangeDefinitions", DefaultValueHandling = DefaultValueHandling.Ignore)]
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
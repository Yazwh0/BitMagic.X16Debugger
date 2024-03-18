using BitMagic.Compiler;
using Newtonsoft.Json;
using System.ComponentModel;

namespace BitMagic.X16Debugger;

public class X16DebugProject
{
    /// <summary>
    /// Start the application in stepping mode.
    /// </summary>
    [JsonProperty("startStepping", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Start the application in stepping mode.")]
    public bool StartStepping { get; set; } = true;

    /// <summary>
    /// Main source file.
    /// </summary>
    [JsonProperty("source", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Main source file.")]
    public string Source { get; set; } = "";

    /// <summary>
    /// Directly run the compiled code, or if false compile the source and add it as a file to the SDCard.
    /// </summary>
    [Description("Directly run the compiled code, or if false compile the source and add it as a file to the SDCard.")]
    [JsonProperty("directRun", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public bool DirectRun { get; set; } = false;


    /// <summary>
    /// Run the main application by creating a AUTOBOOT.X16 file. This will not overwrite if the file already exists.
    /// </summary>
    [JsonProperty("autobootRun", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Run the main application by creating a AUTOBOOT.X16 file. This will not overwrite if the file already exists.")]
    public bool AutobootRun { get; set; } = true;

    /// <summary>
    /// Location to save the .prg and other files from the source file on the host. (Not on the sdcard.)
    /// </summary>
    [JsonProperty("outputFolder", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Location to save the .prg and other files from the source file on the host. (Not on the sdcard.)")]
    public string OutputFolder { get; set; } = "";

    /// <summary>
    /// Start address. If omitted or -1, will start the ROM normally from the vector at $fffc.
    /// </summary>
    [JsonProperty("startAddress", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Start address. If omitted or -1, will start the ROM normally from the vector at $fffc.")]
    public int StartAddress { get; set; } = -1;

    /// <summary>
    /// ROM file to use.
    /// </summary>
    [JsonProperty("romFile", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("ROM file to use.")]
    public string RomFile { get; set; } = "";

    /// <summary>
    /// Folder for the official X16 Emulator.
    /// The rom.bin file from this directory will be used if not set by RomFile.
    /// Symbols for the ROM banks will also be loaded from here, using the names from RomBankNames + .sym extension.
    /// </summary>
    [JsonProperty("emulatorDirectory", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description(@"Folder for the official X16 Emulator.
The rom.bin file from this directory will be used if not set by RomFile.
Symbols for the ROM banks will also be loaded from here, using the names from RomBankNames + .sym extension.")]
    public string EmulatorDirectory { get; set; } = "";

    /// <summary>
    /// List of files that can be imported for symbols.
    /// </summary>
    [JsonProperty("symbols", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("List of files that can be imported for symbols.")]
    public SymbolsFile[] Symbols { get; set; } = Array.Empty<SymbolsFile>();

    /// <summary>
    /// Display names for the Rom banks.
    /// </summary>
    [JsonProperty("romBankNames", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Display names for the Rom banks.")]
    public string[] RomBankNames { get; set; } = new string[] { "Kernal", "Keymap", "Dos", "Fat32", "Basic", "Monitor", "Charset", "Codex", "Graph", "Demo", "Audio", "Util", "Bannex", "X16Edit1", "X16Edit2" };

    /// <summary>
    /// Display names for the Ram banks.
    /// </summary>
    [JsonProperty("ramBankNames", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Display names for the Ram banks.")]
    public string[] RamBankNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Machine to load globals from if there is no bmasm source.
    /// </summary>
    [JsonProperty("machine", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Machine to load globals from if there is no bmasm source.")]
    public string Machine { get; set; } = "";

    /// <summary>
    /// Prefill the keyboard buffer with this data. 16bytes max, rest are discarded.
    /// </summary>
    [JsonProperty("keyboardBuffer", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Prefill the keyboard buffer with this data. 16bytes max, rest are discarded.")]
    public byte[] KeyboardBuffer { get; set; } = new byte[] { };

    /// <summary>
    /// Prefill the mouse buffer with this data. 8bytes max, rest are discarded.
    /// </summary>
    [JsonProperty("mouseBuffer", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Prefill the mouse buffer with this data. 8bytes max, rest are discarded.")]
    public byte[] MouseBuffer { get; set; } = new byte[] { };

    /// <summary>
    /// RTC NvRam Data
    /// </summary>
    [JsonProperty("nvRam", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("RTC NvRam Data")]
    public RtcNvram NvRam { get; set; } = new RtcNvram();

    /// <summary>
    /// SD Card image to start with
    /// </summary>
    [JsonProperty("sdCard", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("SD Card image to start with")]
    public string SdCard { get; set; } = "";

    /// <summary>
    /// Files to add to the root directory of the SDCard. Wildcards accepted.
    /// </summary>
    [JsonProperty("sdCardFiles", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Files to add to the root directory of the SDCard. Wildcards accepted.")]
    public string[] SdCardFiles { get; set; } = new string[] { };

    /// <summary>
    /// Cartridge file to load.
    /// </summary>
    [JsonProperty("cartridge", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Cartridge file to load.")]
    public string Cartridge { get; set; } = "";

    /// <summary>
    /// Compilation Options.
    /// </summary>
    [JsonProperty("compileOptions", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Compilation Options.")]
    public CompileOptions? CompileOptions { get; set; } = null;

    /// <summary>
    /// Value to fill CPU RAM and VRAM with at startup.
    /// </summary>
    [JsonProperty("memoryFillValue", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Value to fill CPU RAM and VRAM with at startup.")]
    public byte MemoryFillValue { get; set; } = 0;

    /// <summary>
    /// Base Path, should try to use this for all other paths.
    /// </summary>
    [JsonProperty("basePath", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Base Path, should try to use this for all other paths.")]
    public string BasePath { get; set; } = "";
}

[JsonObject("rtcNvram")]
public class RtcNvram
{
    /// <summary>
    /// Filename to load into 0x00 -> 0x60 in the RTCs NVRAM.
    /// Not used if Data has values.
    /// </summary>
    [JsonProperty("file", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description(@"Filename to load into 0x00 -> 0x60 in the RTCs NVRAM.
 Not used if Data has values.")]
    public string File { get; set; } = "";

    /// <summary>
    /// Data to load into 0x00 -> 0x60 in the RTCs NVRAM.
    /// </summary>
    [Description("Data to load into 0x00 -> 0x60 in the RTCs NVRAM.")]
    [JsonProperty("data", DefaultValueHandling = DefaultValueHandling.Ignore)]
    public byte[] Data { get; set; } = new byte[] { };

    /// <summary>
    /// Filename to store the RTCs NVRAM in. This will overwrite.
    /// </summary>
    [JsonProperty("writeFile", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Filename to store the RTCs NVRAM in. This will overwrite.")]
    public string WriteFile { get; set; } = "";
}

[JsonObject("symbolsFile")]
public class SymbolsFile
{
    /// <summary>
    /// File name.
    /// </summary>
    [JsonProperty("symbols", DefaultValueHandling = DefaultValueHandling.Ignore, Required = Required.Always)]
    [Description("File name.")]
    public string Symbols { get; set; } = "";

    /// <summary>
    /// ROM bank that the symbols are for. Omit if not a rombank file. Any symbols in the ROM area will be discarded.
    /// </summary>
    [JsonProperty("romBank", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("ROM bank that the symbols are for. Omit if not a rombank file. Any symbols in the ROM area will be discarded.")]
    public int? RomBank { get; set; } = null;

    /// <summary>
    /// RAM bank that the symbols are for. Omit if not a rambank file. Any symbols in the RAM area will be discarded.
    /// </summary>
    [JsonProperty("ramBank", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("RAM bank that the symbols are for. Omit if not a rambank file. Any symbols in the RAM area will be discarded.")]
    public int? RamBank { get; set; } = null;

    /// <summary>
    /// X16 Filename that the symbols are for. Omit if not a X16 binary.
    /// </summary>
    [JsonProperty("filename", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("X16 Filename that the symbols are for. Omit if not a X16 binary.")]
    public string Filename { get; set; } = "";

    /// <summary>
    /// Range of memory that is a jump table. Used to create extra symbols.
    /// </summary>
    [JsonProperty("rangeDefinitions", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Range of memory that is a jump table. Used to create extra symbols.")]
    public RangeDefinition[] RangeDefinitions { get; set; } = Array.Empty<RangeDefinition>();
}

[JsonObject("rangeDefinition")]
public class RangeDefinition
{
    /// <summary>
    /// Start address of the jump table.
    /// </summary>
    [JsonProperty("start", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Start address of the jump table.")]
    public string Start { get; set; } = "";

    /// <summary>
    /// End address of the jump table.
    /// </summary>
    [JsonProperty("end", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("End address of the jump table.")]
    public string End { get; set; } = "";

    /// <summary>
    /// Type of definition, supported : 'jumptable'
    /// </summary>
    [JsonProperty("type", DefaultValueHandling = DefaultValueHandling.Ignore)]
    [Description("Type of definition, supported : 'jumptable'")]
    public string Type { get; set; } = "jumptable";
}
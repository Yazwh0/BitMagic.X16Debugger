using BitMagic.Compiler;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel;

namespace BitMagic.X16Debugger;

public class X16DebugProject
{
    /// <summary>
    /// Start the application in stepping mode.
    /// </summary>
    [JsonProperty("startStepping")]
    [Description("Start the application in stepping mode.")]
    public bool StartStepping { get; set; } = true;

    /// <summary>
    /// Main source file.
    /// </summary>
    [JsonProperty("source")]
    [Description("Main source file.")]
    public string Source { get; set; } = "";

    /// <summary>
    /// Directly run the compiled code, or if false compile the source and add it as a file to the SDCard.
    /// </summary>
    [Description("Directly run the compiled code, or if false compile the source and add it as a file to the SDCard.")]
    [JsonProperty("directRun")]
    public bool DirectRun { get; set; } = false;


    /// <summary>
    /// Run the main application by creating a AUTOBOOT.X16 file. This will not overwrite if the file already exists.
    /// </summary>
    [JsonProperty("autobootRun")]
    [Description("Run the main application by creating a AUTOBOOT.X16 file. This will not overwrite if the file already exists.")]
    public bool AutobootRun { get; set; } = true;

    /// <summary>
    /// Run the named file by creating a AUTOBOOT.X16 file. This will not overwrite if the file already exists. Will override AutobootRun.
    /// </summary>
    [JsonProperty("autobootFile")]
    [Description("Run the named file by creating a AUTOBOOT.X16 file. This will not overwrite if the file already exists. Will override AutobootRun.")]
    public string AutobootFile { get; set; } = "";

    /// <summary>
    /// Location to save the .prg and other files from the source file on the host. (Not on the sdcard.)
    /// </summary>
    [JsonProperty("outputFolder")]
    [Description("Location to save the .prg and other files from the source file on the host. (Not on the sdcard.)")]
    public string OutputFolder { get; set; } = "";

    /// <summary>
    /// Start address. If omitted or -1, will start the ROM normally from the vector at $fffc.
    /// </summary>
    [JsonProperty("startAddress")]
    [Description("Start address. If omitted or -1, will start the ROM normally from the vector at $fffc.")]
    public int StartAddress { get; set; } = -1;

    /// <summary>
    /// ROM file to use.
    /// </summary>
    [JsonProperty("romFile")]
    [Description("ROM file to use.")]
    public string RomFile { get; set; } = "";

    /// <summary>
    /// Folder for the official X16 Emulator.
    /// The rom.bin file from this directory will be used if not set by RomFile.
    /// Symbols for the ROM banks will also be loaded from here, using the names from RomBankNames + .sym extension.
    /// </summary>
    [JsonProperty("emulatorDirectory")]
    [Description(@"Folder for the official X16 Emulator.
The rom.bin file from this directory will be used if not set by RomFile.
Symbols for the ROM banks will also be loaded from here, using the names from RomBankNames + .sym extension.")]
    public string EmulatorDirectory { get; set; } = "";

    /// <summary>
    /// List of files that can be imported for symbols.
    /// </summary>
    [JsonProperty("symbols")]
    [Description("List of files that can be imported for symbols.")]
    public SymbolsFile[] Symbols { get; set; } = Array.Empty<SymbolsFile>();

    /// <summary>
    /// Display names for the Rom banks.
    /// </summary>
    [JsonProperty("romBankNames")]
    [Description("Display names for the Rom banks.")]
    public string[] RomBankNames { get; set; } = new string[] { "Kernal", "Keymap", "Dos", "Fat32", "Basic", "Monitor", "Charset", "Diag", "Graph", "Demo", "Audio", "Util", "Bannex", "X16Edit1", "X16Edit2", "Basload" };

    /// <summary>
    /// Symbol file for Rom banks, if set it overrides the default.
    /// </summary>
    [JsonProperty("romBankSymbols")]
    [Description("Symbol file for Rom banks, if set it overrides the default.")]
    public string[] RomBankSymbols { get; set; } = new string[] { };

    /// <summary>
    /// Display names for the Ram banks.
    /// </summary>
    [JsonProperty("ramBankNames")]
    [Description("Display names for the Ram banks.")]
    public string[] RamBankNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Machine to load globals from if there is no bmasm source.
    /// </summary>
    [JsonProperty("machine")]
    [Description("Machine to load globals from if there is no bmasm source.")]
    public string Machine { get; set; } = "CommanderX16";

    /// <summary>
    /// Prefill the keyboard buffer with this data. 16bytes max, rest are discarded.
    /// </summary>
    [JsonProperty("keyboardBuffer")]
    [Description("Prefill the keyboard buffer with this data. 16bytes max, rest are discarded.")]
    public byte[] KeyboardBuffer { get; set; } = new byte[] { };

    /// <summary>
    /// Prefill the mouse buffer with this data. 8bytes max, rest are discarded.
    /// </summary>
    [JsonProperty("mouseBuffer")]
    [Description("Prefill the mouse buffer with this data. 8bytes max, rest are discarded.")]
    public byte[] MouseBuffer { get; set; } = new byte[] { };

    /// <summary>
    /// RTC NvRam Data.
    /// </summary>
    [JsonProperty("nvRam")]
    [Description("RTC NvRam Data.")]
    public RtcNvram NvRam { get; set; } = new RtcNvram();

    /// <summary>
    /// SD Card image to start with.
    /// </summary>
    [JsonProperty("sdCard")]
    [Description("SD Card image to start with.")]
    public string SdCard { get; set; } = "";

    /// <summary>
    /// Files to add to the root directory of the SDCard. Wildcards accepted.
    /// </summary>
    [JsonProperty("sdCardFiles")]
    [Description("Files to add to the root directory of the SDCard. Wildcards accepted.")]
    public SdCardFile[] SdCardFiles { get; set; } = Array.Empty<SdCardFile>();

    /// <summary>
    /// Cartridge file to load.
    /// </summary>
    [JsonProperty("cartridge")]
    [Description("Cartridge file to load.")]
    public string Cartridge { get; set; } = "";

    /// <summary>
    /// Compilation Options.
    /// </summary>
    [JsonProperty("compileOptions")]
    [Description("Compilation Options.")]
    public CompileOptions? CompileOptions { get; set; } = null;

    /// <summary>
    /// Value to fill CPU RAM and VRAM with at startup.
    /// </summary>
    [JsonProperty("memoryFillValue")]
    [Description("Value to fill CPU RAM and VRAM with at startup.")]
    public byte MemoryFillValue { get; set; } = 0;

    /// <summary>
    /// Base Path, should try to use this for all other paths.
    /// </summary>
    [JsonProperty("basePath")]
    [Description("Base Path, should try to use this for all other paths.")]
    public string BasePath { get; set; } = "";

    /// <summary>
    /// Files to be debugged.
    /// </summary>
    [JsonProperty("files")]
    [Description("Files to be debugged.")]
    [JsonConverter(typeof(DebugProjectFileConverter))]
    public IDebugProjectFile[] Files { get; set; } = [];

    /// <summary>
    /// Breakpoints to be set at system startup.
    /// </summary>
    [JsonProperty("breakpoints")]
    [Description("Breakpoints to be set at system startup.")]
    public int[] Breakpoints { get; set; } = Array.Empty<int>();

    /// <summary>
    /// Size of the history buffer for the history view. Must be a power of 2.
    /// </summary>
    [JsonProperty("historySize")]
    [Description("Size of the history buffer for the history view. Must be a power of 2.")]
    public int HistorySize { get; set; } = 0x400;

    /// <summary>
    /// Multiplier to scale the display window.
    /// </summary>
    [JsonProperty("windowScale")]
    [Description("Multiplier to scale the display window.")]
    public float WindowScale { get; set; } = 0x01;
}


public class SdCardFile
{
    public string Source { get; set; } = "";
    public string Dest { get; set; } = "";

    public bool AllowOverwrite { get; set; } = true;
}

public class DebugProjectFileConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) => typeof(IDebugProjectFile).IsAssignableFrom(objectType);

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var array = JToken.Load(reader);
        var toReturn = new List<IDebugProjectFile>();

        foreach (var i in array.Children())
        {
            JObject obj = JObject.Load(i.CreateReader());

            IDebugProjectFile? item = obj["type"]?.ToString() switch
            {
                "cc65" => new Cc65InputFile(),
                "bitmagic" => new BitmagicInputFile(),
                _ => null
            };

            if (item == null)
                return null;

            serializer.Populate(obj.CreateReader(), item);
            toReturn.Add(item);

        }

        return toReturn.ToArray();
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        serializer.Serialize(writer, value); // not used.
    }
}

public interface IDebugProjectFile
{
    [JsonProperty("type")]
    public string Type { get; }
}

public class Cc65InputFile : IDebugProjectFile
{
    [JsonProperty("type")]
    public string Type { get; set; } = "";

    [JsonProperty("filename")]
    public string Filename { get; set; } = "";

    [JsonProperty("objectFile")]
    public string ObjectFile { get; set; } = "";

    [JsonProperty("config")]
    public string Config { get; set; } = "";

    [JsonProperty("sourcePath")]
    public string SourcePath { get; set; } = "";

    [JsonProperty("startAddress")]
    public int StartAddress { get; set; }

    [JsonProperty("includes")]
    public string[] Includes { get; set; } = Array.Empty<string>();

    [JsonProperty("basepath")]
    public string BasePath { get; set; } = "";
}

public class BitmagicInputFile : IDebugProjectFile
{
    [JsonProperty("type")]
    public string Type { get; set; } = "";

    [JsonProperty("filename")]
    public string Filename { get; set; } = "";
}

[JsonObject("rtcNvram")]
public class RtcNvram
{
    /// <summary>
    /// Filename to load into 0x00 -> 0x60 in the RTCs NVRAM.
    /// Not used if Data has values.
    /// </summary>
    [JsonProperty("file")]
    [Description(@"Filename to load into 0x00 -> 0x60 in the RTCs NVRAM.
 Not used if Data has values.")]
    public string File { get; set; } = "";

    /// <summary>
    /// Data to load into 0x00 -> 0x60 in the RTCs NVRAM.
    /// </summary>
    [Description("Data to load into 0x00 -> 0x60 in the RTCs NVRAM.")]
    [JsonProperty("data")]
    public byte[] Data { get; set; } = new byte[] { };

    /// <summary>
    /// Filename to store the RTCs NVRAM in. This will overwrite.
    /// </summary>
    [JsonProperty("writeFile")]
    [Description("Filename to store the RTCs NVRAM in. This will overwrite.")]
    public string WriteFile { get; set; } = "";
}

[JsonObject("symbolsFile")]
public class SymbolsFile
{
    /// <summary>
    /// File name.
    /// </summary>
    [JsonProperty("symbols", Required = Required.Always)]
    [Description("File name.")]
    public string Symbols { get; set; } = "";

    /// <summary>
    /// ROM bank that the symbols are for. Omit if not a rombank file. If set any symbols in the ROM area will be discarded.
    /// </summary>
    [JsonProperty("romBank")]
    [Description("ROM bank that the symbols are for. Omit if not a rombank file. If set any symbols in the ROM area will be discarded.")]
    public int? RomBank { get; set; } = null;

    /// <summary>
    /// RAM bank that the symbols are for. Omit if not a rambank file. If set any symbols in the RAM area will be discarded.
    /// </summary>
    [JsonProperty("ramBank")]
    [Description("RAM bank that the symbols are for. Omit if not a rambank file. If set any symbols in the RAM area will be discarded.")]
    public int? RamBank { get; set; } = null;

    /// <summary>
    /// X16 Filename that the symbols are for. Omit if not a X16 binary.
    /// </summary>
    [JsonProperty("filename")]
    [Description("X16 Filename that the symbols are for. Omit if not a X16 binary.")]
    public string Filename { get; set; } = "";

    /// <summary>
    /// Range of memory that is a jump table. Used to create extra symbols.
    /// </summary>
    [JsonProperty("rangeDefinitions")]
    [Description("Range of memory that is a jump table. Used to create extra symbols.")]
    public RangeDefinition[] RangeDefinitions { get; set; } = Array.Empty<RangeDefinition>();
}

[JsonObject("rangeDefinition")]
public class RangeDefinition
{
    /// <summary>
    /// Start address of the jump table.
    /// </summary>
    [JsonProperty("start")]
    [Description("Start address of the jump table.")]
    public string Start { get; set; } = "";

    /// <summary>
    /// End address of the jump table.
    /// </summary>
    [JsonProperty("end")]
    [Description("End address of the jump table.")]
    public string End { get; set; } = "";

    /// <summary>
    /// Type of definition, supported : 'jumptable'
    /// </summary>
    [JsonProperty("type")]
    [Description("Type of definition, supported : 'jumptable'")]
    public string Type { get; set; } = "jumptable";
}
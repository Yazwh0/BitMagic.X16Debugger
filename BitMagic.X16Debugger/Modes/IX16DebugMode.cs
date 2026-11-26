using BitMagic.Common;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;

namespace BitMagic.X16Debugger.Modes;

/// <summary>
/// A debug mode changes what X16D debugs, eg BASIC rather than the 6502 code running it. It's loaded from
/// its own assembly (see <see cref="DebugModeSettings"/>), is told when its system breakpoints are hit,
/// and answers the DAP requests it owns. Whatever it returns null for is handled by X16D as normal, so
/// the memory views, disassembly and the rest keep working alongside it.
///
/// <see cref="OnSystemBreakpoint"/> is called on the emulator thread while the emulator is stopped. The
/// request methods are called on the protocol thread, normally while stopped.
/// </summary>
public interface IX16DebugMode
{
    string Name { get; }

    /// <summary>
    /// Implement as <c>=&gt; DebugModeApi.Version;</c>. The constant is compiled into the mode, so X16D can tell
    /// which version of this interface it was built against, and refuse one built for a different version.
    /// </summary>
    int ApiVersion { get; }

    /// <summary>
    /// Called once the ROM, symbols and project files are loaded, before the emulator starts.
    /// </summary>
    /// <param name="settings">The <c>settings</c> object from <see cref="DebugModeSettings"/>, if any.</param>
    void Launch(IX16DebugHost host, JObject? settings);

    /// <summary>
    /// A breakpoint set with <see cref="IX16DebugHost.SetSystemBreakpoint"/> was hit.
    /// </summary>
    DebugModeStop OnSystemBreakpoint(int debuggerAddress);

    SetBreakpointsResponse? SetBreakpoints(SetBreakpointsArguments arguments);
    StackTraceResponse? StackTrace(StackTraceArguments arguments);
    ScopesResponse? Scopes(ScopesArguments arguments);
    VariablesResponse? Variables(VariablesArguments arguments);
    SetVariableResponse? SetVariable(SetVariableArguments arguments);
    EvaluateResponse? Evaluate(EvaluateArguments arguments);
    SourceResponse? Source(SourceArguments arguments);

    /// <summary>
    /// Step into, over, out or pause. Return true if the mode will stop by itself (from a system
    /// breakpoint), and X16D should just let the emulator run; false to have X16D step the CPU as normal.
    /// </summary>
    bool Step(DebugModeStep step);

    /// <summary>
    /// The user continued, so any step the mode had armed is cancelled.
    /// </summary>
    void Continue();

    /// <summary>
    /// An <c>x16Mode</c> request, from the mode's own client (eg its VS Code extension). Can be called
    /// while the emulator is running. Return null for a command the mode doesn't know.
    /// </summary>
    JToken? CustomRequest(string command, JObject? arguments) => null;
}

public static class DebugModeApi
{
    /// <summary>The version of <see cref="IX16DebugMode"/> and <see cref="IX16DebugHost"/>; goes up when either changes.</summary>
    public const int Version = 1;
}

public enum DebugModeStep
{
    Into,
    Over,
    Out,
    Pause
}

/// <summary>
/// What a mode wants done at one of its system breakpoints.
/// </summary>
public sealed record DebugModeStop(bool Stop, StoppedEvent.ReasonValue Reason = StoppedEvent.ReasonValue.Breakpoint,
    string? Description = null, IReadOnlyList<int>? HitBreakpointIds = null, string? ExceptionMessage = null,
    bool Terminate = false)
{
    public static DebugModeStop Continue { get; } = new(false);

    public static DebugModeStop Breakpoint(string description, IReadOnlyList<int>? ids = null) =>
        new(true, StoppedEvent.ReasonValue.Breakpoint, description, ids);

    public static DebugModeStop Step(string description = "Step") =>
        new(true, StoppedEvent.ReasonValue.Step, description);

    public static DebugModeStop Pause() => new(true, StoppedEvent.ReasonValue.Pause, "Paused");

    public static DebugModeStop Exception(string message) =>
        new(true, StoppedEvent.ReasonValue.Exception, message, ExceptionMessage: message);

    /// <summary>End the debug session, eg because the program finished.</summary>
    public static DebugModeStop End(string description) => new(false, Description: description, Terminate: true);
}

/// <summary>
/// What X16D gives a debug mode.
/// </summary>
public interface IX16DebugHost
{
    Emulator Emulator { get; }
    IEmulatorLogger Logger { get; }

    /// <summary>The project's base path, for resolving the mode's own files.</summary>
    string BasePath { get; }

    /// <summary>A new id, unique among X16D's own (for variable references, sources and breakpoints).</summary>
    int NewId();

    void SendEvent(DebugEvent debugEvent);

    /// <summary>
    /// Sets or clears a breakpoint that calls <see cref="IX16DebugMode.OnSystemBreakpoint"/> rather than
    /// stopping. <paramref name="debuggerAddress"/> includes the bank, eg 0x04CC36 for $CC36 in ROM bank 4.
    /// </summary>
    void SetSystemBreakpoint(int debuggerAddress, bool set = true);

    /// <summary>The symbols file loaded for a ROM bank, if there is one.</summary>
    string? RomSymbolsFile(int bank);
}

/// <summary>
/// <c>"debugMode"</c> in the project file or the launch arguments.
/// </summary>
public sealed class DebugModeSettings
{
    /// <summary>
    /// A NuGet package holding the mode, eg <c>BitMagic.X16Basic.Debugger</c>. It's downloaded once into a
    /// cache. The mode is in the assembly named after the package, unless <see cref="Assembly"/> says otherwise.
    /// </summary>
    [Newtonsoft.Json.JsonProperty("package")]
    public string? Package { get; set; }

    /// <summary>The package's version: exact, a range or float (eg <c>0.2.*</c>), or left out for the latest release.</summary>
    [Newtonsoft.Json.JsonProperty("version")]
    public string? Version { get; set; }

    /// <summary>Where to get the package from: a NuGet feed or a folder. nuget.org if left out.</summary>
    [Newtonsoft.Json.JsonProperty("source")]
    public string? Source { get; set; }

    /// <summary>
    /// Without a <see cref="Package"/>, the assembly holding the mode (relative paths are from the project's
    /// base path), eg while developing one. With a package, the assembly's name in it.
    /// </summary>
    [Newtonsoft.Json.JsonProperty("assembly")]
    public string Assembly { get; set; } = "";

    /// <summary>The type to use, if the assembly has more than one <see cref="IX16DebugMode"/>.</summary>
    [Newtonsoft.Json.JsonProperty("type")]
    public string? Type { get; set; }

    /// <summary>Passed to <see cref="IX16DebugMode.Launch"/>.</summary>
    [Newtonsoft.Json.JsonProperty("settings")]
    public JObject? Settings { get; set; }
}

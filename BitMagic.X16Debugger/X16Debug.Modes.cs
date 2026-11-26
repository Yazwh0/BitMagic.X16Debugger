using BitMagic.Common;
using BitMagic.Common.Address;
using BitMagic.X16Debugger.Modes;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Utilities;
using Newtonsoft.Json.Linq;

namespace BitMagic.X16Debugger;

// The debug mode plumbing, see Modes/IX16DebugMode.cs.
public partial class X16Debug
{
    private IX16DebugMode? _mode;
    private ModeHost? _modeHost;

    // reset while a launch with a debug mode is in progress
    private readonly ManualResetEventSlim _modeReady = new(true);

    private static bool LaunchHasDebugMode(LaunchArguments arguments)
    {
        if (arguments.ConfigurationProperties.ContainsKey("debugMode"))
            return true;

        var program = arguments.ConfigurationProperties.GetValueAsString("program");
        try
        {
            return program != null && program.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(program) && File.ReadAllText(program).Contains("\"debugMode\"");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Waits for a launch that's loading a debug mode, so its breakpoints go to the mode.
    /// </summary>
    private void WaitForDebugMode() => _modeReady.Wait(TimeSpan.FromSeconds(120));

    /// <summary>
    /// Loads the debug mode from the launch arguments' <c>debugMode</c>, or the project's. Called once
    /// everything else is loaded.
    /// </summary>
    private void LoadDebugMode(LaunchArguments arguments)
    {
        var settings = _debugProject?.DebugMode;
        if (arguments.ConfigurationProperties.TryGetValue("debugMode", out var token) && token is JObject obj)
            settings = obj.ToObject<DebugModeSettings>();

        if (settings == null)
            return;

        try
        {
            _mode = DebugModeLoader.Load(settings, _debugProject!.BasePath, Logger);
            _modeHost = new ModeHost(this);
            Logger.LogLine($"Debug mode '{_mode.Name}'.");
            _mode.Launch(_modeHost, settings.Settings);
        }
        catch (Exception e)
        {
            _mode = null;
            _modeHost?.ClearAll();
            _modeHost = null;
            throw new ProtocolException($"Debug mode failed to start: {e.Message}");
        }
    }

    private void UnloadDebugMode()
    {
        _modeHost?.ClearAll();
        _modeHost = null;
        _mode = null;
    }

    /// <summary>
    /// If the emulator has stopped on one of the mode's system breakpoints, asks the mode what to do.
    /// Returns true if it was the mode's.
    /// </summary>
    private CustomMessage.ModeRequestResponse HandleModeRequest(CustomMessage.ModeRequestArguments arguments)
    {
        if (_mode == null)
            throw new ProtocolException("There's no debug mode.");

        try
        {
            return new CustomMessage.ModeRequestResponse { Result = _mode.CustomRequest(arguments.Command, arguments.Arguments) };
        }
        catch (Exception e) when (e is not ProtocolException)
        {
            throw new ProtocolException(e.Message);
        }
    }

    // what the mode decided in ModeCarriesOn, when it wasn't to carry on, for HandleModeBreakpoint
    private DebugModeStop? _modeDecision;

    /// <summary>
    /// Called straight after the emulator stops. If it's only for one of the mode's system breakpoints and
    /// the mode wants to carry on, returns true so the debug loop goes straight back into the emulator,
    /// skipping the change tracking and the rest: a mode like BASIC's is called for every statement.
    /// </summary>
    private bool ModeCarriesOn(Emulator.EmulatorResult result)
    {
        _modeDecision = null;

        if (_mode == null || _modeHost == null || result != Emulator.EmulatorResult.Breakpoint ||
            _emulator.BreakpointSource != Emulator.BreakpointSourceType.Breakpoint ||
            (_emulator.Breakpoints[_emulator.Pc] & DebugConstants.Breakpoint) != 0)
            return false;

        var debuggerAddress = AddressFunctions.GetDebuggerAddress(_emulator.Pc, _emulator);
        if (!_modeHost.Owns(debuggerAddress))
            return false;

        var decision = AskMode(debuggerAddress);
        if (!decision.Stop && !decision.Terminate && !_emulator.Stepping)
            return true;

        _modeDecision = decision;
        return false;
    }

    private DebugModeStop AskMode(int debuggerAddress)
    {
        try
        {
            return _mode!.OnSystemBreakpoint(debuggerAddress);
        }
        catch (Exception e)
        {
            Logger.LogError($"Debug mode error: {e.Message}");
            return DebugModeStop.Exception($"Debug mode error: {e.Message}");
        }
    }

    private bool HandleModeBreakpoint()
    {
        if (_mode == null || _modeHost == null)
            return false;

        // already asked, see ModeCarriesOn; the mode may have cleared the breakpoint since, eg a one shot
        var decision = _modeDecision;
        _modeDecision = null;

        if (decision == null)
        {
            var debuggerAddress = AddressFunctions.GetDebuggerAddress(_emulator.Pc, _emulator);
            if (!_modeHost.Owns(debuggerAddress))
                return false;
            decision = AskMode(debuggerAddress);
        }

        if (decision.Terminate)
        {
            Logger.LogLine(decision.Description ?? "Finished.");
            Protocol.SendEvent(new ExitedEvent(0));
            Protocol.SendEvent(new TerminatedEvent());
            _running = false;
            _emulator.Stepping = true; // park until the client disconnects
            return true;
        }

        if (!decision.Stop)
            return true;

        if (decision.ExceptionMessage != null)
        {
            _serviceManager.ExceptionManager.LastException = "MODE";
            _serviceManager.ExceptionManager.LastExceptionMessage = decision.ExceptionMessage;
        }

        var stopped = new StoppedEvent(decision.Reason, decision.Description ?? "Stopped", 0, null, true);
        if (decision.HitBreakpointIds != null && decision.HitBreakpointIds.Count > 0)
            stopped.HitBreakpointIds = decision.HitBreakpointIds.ToList();

        Protocol.SendEvent(stopped);
        _emulator.Stepping = true;
        return true;
    }

    /// <summary>
    /// Lets the mode take a step request. Returns true if it did, and the emulator has been set running.
    /// </summary>
    private bool ModeStep(DebugModeStep step)
    {
        if (_mode == null || !_mode.Step(step))
            return false;

        _emulator.Stepping = false;
        lock (SyncObject)
        {
            _runEvent.Set();
        }
        return true;
    }

    private sealed class ModeHost(X16Debug debug) : IX16DebugHost
    {
        private readonly HashSet<int> _owned = new();

        public Emulator Emulator => debug._emulator;
        public IEmulatorLogger Logger => debug.Logger;
        public string BasePath => debug._debugProject?.BasePath ?? "";

        public int NewId() => debug._serviceManager.IdManager.GetId();

        public void SendEvent(DebugEvent debugEvent) => debug.Protocol.SendEvent(debugEvent);

        public bool Owns(int debuggerAddress)
        {
            lock (_owned)
                return _owned.Contains(debuggerAddress);
        }

        public void SetSystemBreakpoint(int debuggerAddress, bool set = true)
        {
            var breakpoints = debug._serviceManager.BreakpointManager;
            lock (_owned)
            {
                if (set)
                {
                    _owned.Add(debuggerAddress);
                    breakpoints.DebuggerBreakpoints.Add(debuggerAddress);
                    breakpoints.SetDebuggerBreakpoints();
                    return;
                }

                _owned.Remove(debuggerAddress);
                breakpoints.DebuggerBreakpoints.Remove(debuggerAddress);
            }

            var (address, bank) = AddressFunctions.GetAddressBank(debuggerAddress);
            var (primary, secondary) = AddressFunctions.GetMemoryLocations(bank, address);
            var currentBank = address >= 0xc000 ? Emulator.Memory[0x01] : Emulator.Memory[0x00];
            if (primary < 0xa000 || bank == currentBank)
                Emulator.Breakpoints[primary] &= ~DebugConstants.SystemBreakpoint;
            if (secondary != 0)
                Emulator.Breakpoints[secondary] &= ~DebugConstants.SystemBreakpoint;
        }

        public void ClearAll()
        {
            int[] owned;
            lock (_owned)
                owned = _owned.ToArray();
            foreach (var address in owned)
                SetSystemBreakpoint(address, false);
        }

        public string? RomSymbolsFile(int bank)
        {
            var symbols = debug._debugProject?.Symbols.FirstOrDefault(s => s.RomBank == bank)?.Symbols;
            if (string.IsNullOrWhiteSpace(symbols))
                return null;
            return Path.IsPathRooted(symbols) ? symbols : Path.GetFullPath(symbols, BasePath);
        }
    }
}

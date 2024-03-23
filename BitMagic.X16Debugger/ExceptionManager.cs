using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger;

internal class ExceptionManager
{
    private readonly Emulator _emulator;
    private readonly Dictionary<string, Breakpoint> _breakpoints;
    private readonly Dictionary<string, Breakpoint> _setBreakpoints = new();

    public string LastException { get; set; } = "";

    public ExceptionManager(Emulator emulator, IdManager idManager)
    {
        _emulator = emulator;
        _emulator.Brk_Causes_Stop = false;
        _breakpoints = new Dictionary<string, Breakpoint>()
        {
            { "BRK", new Breakpoint() { Id = idManager.GetId(), Verified = true } },
            { "EXP", new Breakpoint() { Id = idManager.GetId(), Verified = true } }
        };
    }

    public static List<ExceptionBreakpointsFilter> GetExceptionList() => new()
    {
        new ExceptionBreakpointsFilter()
        {
            Filter = "BRK",
            Label = "BRK Hit",
            Description = "Raise exception when a BRK is hit.",
            Default = false,
            SupportsCondition = false,
            ConditionDescription = ""
        },
        new ExceptionBreakpointsFilter()
        {
            Filter = "EXP",
            Label = "Code Exception",
            Description = "Exception raised within code.",
            Default = true,
            SupportsCondition = false,
            ConditionDescription = ""
        }
    };

    public bool IsSet(string filter) => _setBreakpoints.ContainsKey(filter);

    public SetExceptionBreakpointsResponse SetExceptionBreakpointsRequest(SetExceptionBreakpointsArguments arguments)
    {
        var toReturn = new SetExceptionBreakpointsResponse();

        _setBreakpoints.Clear();

        foreach (var i in arguments.Filters)
        {
            switch (i)
            {
                case "BRK":
                case "EXP":
                    _setBreakpoints.Add(i, _breakpoints[i]);
                    break;
                default:
                    toReturn.Breakpoints.Add(new Breakpoint()
                    {
                        Message = $"Unknown exception {i}",
                        Verified = false
                    });
                    break;
            }
        }

        _emulator.Brk_Causes_Stop = _setBreakpoints.ContainsKey("BRK");

        return toReturn;
    }

    public ExceptionInfoResponse ExceptionInfoRequest(ExceptionInfoArguments _) =>
        LastException switch
        {
            "BRK" => new ExceptionInfoResponse() { Description = "BRK has been hit.", ExceptionId = "BRK" },
            "EXP" => new ExceptionInfoResponse() { Description = "Exception raised within code.", ExceptionId = "EXP" },
            _ => new ExceptionInfoResponse() { Description = "Unknown exception", ExceptionId = "UNK" }
        };

}

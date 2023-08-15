using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;

namespace BitMagic.X16Debugger;

internal class PsgManager
{
    private readonly Emulator _emulator;
    private readonly VariableChildren[] _children = new VariableChildren[16];

    public PsgManager(Emulator emulator)
    {
        _emulator= emulator;
        var voices = _emulator.VeraAudio.PsgVoices;
        for (var i = 0; i < voices.Length; i++)
        {
            var index = i;
            _children[i] = new VariableChildren($"PSG Voice {index}",
                () => _emulator.VeraAudio.PsgVoices[index].LeftRight == 0 ? "Disabled" : "Enabled",
                new []
                {
                    new VariableMap("Waveform", "string", () => GetWaveform(_emulator.VeraAudio.PsgVoices[index].Waveform), () => GetWaveform(_emulator.VeraAudio.PsgVoices[index].Waveform)),
                    new VariableMap("Output", "string", () => GetOutput(_emulator.VeraAudio.PsgVoices[index].LeftRight), () => GetOutput(_emulator.VeraAudio.PsgVoices[index].LeftRight)),
                    new VariableMap("Volume", "int", () => _emulator.VeraAudio.PsgVoices[index].Volume.ToString(), () => _emulator.VeraAudio.PsgVoices[index].Volume),
                    new VariableMap("Frequency", "int", () => _emulator.VeraAudio.PsgVoices[index].Frequency.ToString(), () => _emulator.VeraAudio.PsgVoices[index].Frequency),
                    new VariableMap("Width", "int", () => _emulator.VeraAudio.PsgVoices[index].Width.ToString(), () => _emulator.VeraAudio.PsgVoices[index].Width),
                    new VariableMap("Value", "int", () => _emulator.VeraAudio.PsgVoices[index].Value.ToString(), () => _emulator.VeraAudio.PsgVoices[index].Value),
                    new VariableMap("Phase", "int", () => _emulator.VeraAudio.PsgVoices[index].Phase.ToString(), () => _emulator.VeraAudio.PsgVoices[index].Phase),
                    new VariableMap("Noise", "int", () => _emulator.VeraAudio.PsgVoices[index].Noise.ToString(), () => _emulator.VeraAudio.PsgVoices[index].Noise),
                });

        }
    }

    public void Register(VariableManager variableManager)
    {
        for(var i = 0; i < _children.Length; i++)
        {
            variableManager.Register(_children[i]);
        }
    }

    public (string Value, ICollection<Variable> Data) GetFunction()
    {
        string value;
        var voices = _emulator.VeraAudio.PsgVoices;
        var variables = new Variable[16];

        var cnt = 0;
        for (var i = 0; i < 16; i++)
        {
            cnt += voices[i].LeftRight != 0 ? 1 : 0;
            variables[i] = _children[i].GetVariable();        // updates the objects
        }

        if (cnt == 0)
            value = "None active";
        else
            value = $"{cnt:0} active";

        return (value, variables);
    }

    private string GetWaveform(uint waveform) => waveform switch
    {
        0 => "Pulse",
        1 => "SawTooth",
        2 => "Triangle",
        3 => "Noise",
        _ => "Unknown"
    };

    private string GetOutput(uint leftright) => leftright switch
    {
        0 => "None",
        1 => "Left",
        2 => "Right",
        3 => "Both",
        _ => "Unknown"
    };
}

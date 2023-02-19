using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BitMagic.X16Debugger;

internal interface ISampleVariableContainer
{
    string Name { get; }
    int VariableReference { get; }

    IReadOnlyCollection<ISampleVariableContainer> ChildContainers { get; }
    IReadOnlyCollection<SampleVariable> Variables { get; }

    VariablesResponse HandleVariablesRequest(VariablesArguments args);
    SetVariableResponse? HandleSetVariableRequest(SetVariableArguments arguments);

    ISampleVariableContainer? Container { get; }
}
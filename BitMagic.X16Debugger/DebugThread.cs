//using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

//namespace BitMagic.X16Debugger;


//internal class DebugThread
//{
//    private Stack<SampleStackFrame> frames;

//    internal DebugThread(int id, string name)
//    {
//        this.Id = id;
//        this.Name = name;

//        this.frames = new Stack<SampleStackFrame>();
//    }

//    internal int Id { get; private set; }
//    internal string Name { get; private set; }

//    internal IReadOnlyCollection<SampleStackFrame> StackFrames
//    {
//        get { return this.frames; }
//    }

//    internal void PushStackFrame(SampleStackFrame frame)
//    {
//        this.frames.Push(frame);
//    }

//    internal SampleStackFrame PopStackFrame()
//    {
//        return this.frames.Pop();
//    }

//    internal SampleStackFrame GetTopStackFrame()
//    {
//        if (this.frames.Any())
//        {
//            return this.frames.Peek();
//        }

//        return null;
//    }

//    internal void Invalidate()
//    {
//        foreach (SampleStackFrame stackFrame in this.frames)
//        {
//            stackFrame.Invalidate();
//        }
//    }

//    #region Protocol Implementation

//    internal Thread GetProtocolThread()
//    {
//        return new Thread(
//            id: this.Id,
//            name: this.Name);
//    }

//    internal StackTraceResponse HandleStackTraceRequest(StackTraceArguments arguments)
//    {
//        IEnumerable<SampleStackFrame> enumFrames = this.frames;

//        if (arguments.StartFrame.HasValue)
//        {
//            enumFrames = enumFrames.Skip(arguments.StartFrame.Value);
//        }

//        if (arguments.Levels.HasValue)
//        {
//            enumFrames = enumFrames.Take(arguments.Levels.Value);
//        }

//        List<StackFrame> stackFrames = enumFrames.Select(f => f.GetProtocolObject(arguments.Format)).ToList();

//        return new StackTraceResponse(
//            stackFrames: stackFrames,
//            totalFrames: this.frames.Count);
//    }

//    #endregion
//}
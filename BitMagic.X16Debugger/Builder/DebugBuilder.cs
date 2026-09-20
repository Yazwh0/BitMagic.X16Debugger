using BitMagic.Common;
using BitMagic.X16Debugger.DebugableFiles;
using BitMagic.X16Debugger.Extensions;
using BitMagic.X16Debugger.LSP;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Debugger.Builder;

internal static class DebugBuilder
{
    public static LaunchResponse? BuildDebugProject(X16DebugProject _debugProject, string workspaceFolder,
        DebugProtocolClient Protocol, Emulator _emulator, ServiceManager _serviceManager, IEmulatorLogger Logger,
        out string autobootFile)
    {
        autobootFile = "";

        try
        {
            if (!ProjectBuildRunner.TryBuild(_debugProject, workspaceFolder, _serviceManager, Logger, out autobootFile))
            {
                Protocol.SendEvent(new TerminatedEvent() { Restart = false });

                return new LaunchResponse();
            }

            _emulator.Pc = _debugProject.StartAddress != -1 ? (ushort)_debugProject.StartAddress : (ushort)((_emulator.RomBank[0x3ffd] << 8) + _emulator.RomBank[0x3ffc]);

            if (_debugProject.DirectRun && !string.IsNullOrWhiteSpace(_debugProject.Source))
            {
                var result = _serviceManager.DebugableFileManager.GetFile_New(_debugProject.Source) ?? throw new Exception("Source file not found");
                var prg = result as IBinaryFile ?? throw new Exception("result is not a IBinaryFile!");

                _emulator.LoadIntoMemory(prg.Data, 0x801, true);
                result.FileLoaded(_emulator, 0x801, true, _serviceManager.SourceMapManager, _serviceManager.DebugableFileManager);

                _emulator.Pc = _debugProject.StartAddress != -1 ? (ushort)_debugProject.StartAddress : (ushort)0x810;
                Logger.LogLine($"Injecting {prg.Data.Count:#,##0} bytes. Starting at 0x801. PC is 0x{_emulator.Pc:X4}.");
            }

            return null;
        }
        catch (Exception e)
        {
            Logger.LogLine($"ERROR: {e.Message}");
            Logger.LogError(e.StackTrace);

            throw new ProtocolException(e.Message);
        }
    }
}

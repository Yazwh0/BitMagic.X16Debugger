using BitMagic.X16Emulator;

namespace BitMagic.X16Debugger.CustomMessage;

// Shared by the standard DAP readMemory/writeMemory handlers and the custom searchMemory
// request, so the memoryReference -> Span<byte> mapping lives in exactly one place.
internal static class MemorySpaceResolver
{
    public static Span<byte> Resolve(string memoryReference, Emulator emulator, out bool recognized)
    {
        recognized = true;

        if (memoryReference.StartsWith("rambank"))
        {
            int.TryParse(memoryReference.Substring(8), out var bank);
            return emulator.RamBank.Slice(bank * 0x2000, 0x2000);
        }

        if (memoryReference.StartsWith("rombank"))
        {
            int.TryParse(memoryReference.Substring(8), out var bank);
            return emulator.RomBank.Slice(bank * 0x4000, 0x4000);
        }

        switch (memoryReference)
        {
            case "main":
                return emulator.Memory;
            case "vram":
                return emulator.Vera.Vram;
            case "sdcard":
                return emulator.SdCard.Image;
            case "sdcardblock":
                return emulator.SpiOutboundBuffer.Slice(0, (int)emulator.Spi.SendLength);
            case "nvram":
                return emulator.RtcNvram;
            default:
                recognized = false;
                return Span<byte>.Empty;
        }
    }

    /// <summary>
    /// Writes as much of the data as fits at the offset, eg writing past the end of the SD card used to throw.
    /// Returns the number of bytes written.
    /// </summary>
    public static int Write(Span<byte> memory, int offset, ReadOnlySpan<byte> toWrite)
    {
        if (offset < 0 || offset >= memory.Length)
            return 0;

        var count = Math.Min(toWrite.Length, memory.Length - offset);
        toWrite[..count].CopyTo(memory[offset..]);

        return count;
    }
}

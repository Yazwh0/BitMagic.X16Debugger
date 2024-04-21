namespace BitMagic.X16Debugger;

internal static class AutobootCreator
{
    public static byte[] GetAutoboot(string prgName)
    {
        const int prgHeaderSize = 2;
        const int lineNumberSize = 2;
        const int addressSize = 2;
        const int tokenSize = 1;
        const int endOfLineSize = 1;
        const int loadParamsSize = 4;
        const int spaceSize = 1;
        const int quoteMarksSize = 1;

        var nextLine = 0x801 + addressSize + lineNumberSize + tokenSize + spaceSize + quoteMarksSize + prgName.Length + quoteMarksSize + loadParamsSize + endOfLineSize;
        var toReturn = new byte[
            prgHeaderSize +
            addressSize + lineNumberSize + tokenSize + spaceSize + quoteMarksSize + prgName.Length + quoteMarksSize + loadParamsSize + endOfLineSize +
            addressSize + lineNumberSize + tokenSize + endOfLineSize +
            addressSize];

        int pos = 0;
        toReturn[pos++] = 0x01; // prg header
        toReturn[pos++] = 0x08;
        toReturn[pos++] = (byte)(nextLine & 0xff);
        toReturn[pos++] = (byte)((nextLine & 0xff00) >> 8);
        toReturn[pos++] = 0x0a; // 10
        toReturn[pos++] = 0x00; // 00 - for line number 000a - 10
        toReturn[pos++] = 0x93; // LOAD
        toReturn[pos++] = 0x20; // SPACE
        toReturn[pos++] = 0x22; // "
        for(var i = 0; i < prgName.Length;i ++)
        {
            toReturn[pos++] = (byte)prgName[i];
        }
        toReturn[pos++] = 0x22; // "

        toReturn[pos++] = 0x2c; // ,
        toReturn[pos++] = 0x38; // 8
        toReturn[pos++] = 0x2c; // ,
        toReturn[pos++] = 0x30; // 0

        toReturn[pos++] = 0x00; // End of line

        nextLine += addressSize + lineNumberSize + tokenSize + endOfLineSize;

        toReturn[pos++] = (byte)(nextLine & 0xff);
        toReturn[pos++] = (byte)((nextLine & 0xff00) >> 8);
        toReturn[pos++] = 0x14; // 20
        toReturn[pos++] = 0x00; // 00 - for line number 0014 - 20
        toReturn[pos++] = 0x8a; // RUN
        toReturn[pos++] = 0x00; // End of line

        toReturn[pos++] = 0x00;
        toReturn[pos++] = 0x00; // No more lines

        return toReturn;
    }
}

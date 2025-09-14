namespace BitMagic.X16Debugger.LSP;

public class LspServer(Stream inputStream, Stream outputStream)
{
    public void Run()
    {
        var reader = new StreamReader(inputStream);

        while (reader.ReadLine() is string line)
        {
            if (line == null)
                return;

            Console.WriteLine($"LSP: {line}");
        }
    }
}

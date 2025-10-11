using System.Text;

namespace BitMagic.X16Debugger.LSP.Logging;

public class LoggingStreamWrapper : Stream
{
    private readonly Stream _innerStream;
    private readonly string _name;

    public LoggingStreamWrapper(Stream innerStream, string name = "Stream")
    {
        _innerStream = innerStream;
        _name = name;
    }

    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => _innerStream.CanSeek;
    public override bool CanWrite => _innerStream.CanWrite;
    public override long Length => _innerStream.Length;
    public override long Position
    {
        get => _innerStream.Position;
        set => _innerStream.Position = value;
    }

    public override void Flush() => _innerStream.Flush();

    public override int Read(byte[] buffer, int offset, int count)
    {
        int bytesRead = _innerStream.Read(buffer, offset, count);
        Log("Read", buffer, offset, bytesRead);
        return bytesRead;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Log("Write", buffer, offset, count);
        _innerStream.Write(buffer, offset, count);
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int bytesRead = await _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
        Log("ReadAsync", buffer, offset, bytesRead);
        return bytesRead;
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Log("WriteAsync", buffer, offset, count);
        await _innerStream.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
    public override void SetLength(long value) => _innerStream.SetLength(value);

    private void Log(string operation, byte[] buffer, int offset, int count)
    {
        string data = Encoding.UTF8.GetString(buffer, offset, count);
        Console.WriteLine($"[{_name}] {operation}: {count} bytes");
        Console.WriteLine($"[{_name}] Data: {data}");
    }
}

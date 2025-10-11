namespace BitMagic.X16Debugger.LSP;

internal class DocumentCache
{
    private readonly Dictionary<string, SourceFile> _files = new();

    public string[] GetFile(string filename)
    {
        if (_files.TryGetValue(filename, out var sourceFile))
            return sourceFile.Lines;

        return [];
    }

    public async Task AddFile(string filename)
    {
        var lines = await File.ReadAllLinesAsync(filename);

        _files.Add(filename, new SourceFile() { Filename = filename, Lines = lines });
    }

    public void SetFileContent(string filename, string[] content)
    {
        if (_files.ContainsKey(filename))
        {
            _files[filename].Lines = content;
            return;
        }

        _files.Add(filename, new SourceFile() { Filename = filename, Lines = content });
    }

    public async Task UpdateFile(string filename)
    {
        _files.Remove(filename, out _);
        await AddFile(filename);
    }

    public bool IsInCache(string filename) => _files.ContainsKey(filename);

    private sealed class SourceFile
    {
        public string Filename { get; set; } = "";
        public string[] Lines { get; set;  } = Array.Empty<string>();
    }
}

using BitMagic.Common;

namespace BitMagic.X16Debugger;

internal class SouceFileManager
{
    private readonly Dictionary<string, ISourceFile> _files = new();

    public SouceFileManager()
    {
    }

    public ISourceFile? GetFile(string path)
    {
        if (_files.ContainsKey(path))
            return _files[path];

        return null;
    }

    public void AddRelatives(ISourceFile sourceFile)
    {
        if (_files.ContainsKey(sourceFile.Path))
            return;

        _files.Add(sourceFile.Path, sourceFile);

        foreach (var p in sourceFile.Parents)
            AddRelatives(p);

        foreach(var c in sourceFile.Children)
            AddRelatives(c);
    }

    public void Reset()
    {
        _files.Clear();
    }
}

using BigMagic.TemplateEngine.Compiler;

namespace BitMagic.X16Debugger;

internal class CodeGeneratorManager
{
    private readonly Dictionary<string, TemplateReference> Templates = new();

    private readonly IdManager _idManager;
    internal CodeGeneratorManager(IdManager idManager)
    {
        _idManager = idManager;
    }

    public int Register(string path, MacroAssembler.ProcessResult templateResult)
    {
        var id = _idManager.AddObject(templateResult, ObjectType.DecompiledData);
        Templates.Add(FixFilename(path), new TemplateReference(id, templateResult));

        return id;
    }

    public int GetSourceReference(string path) => Templates[FixFilename(path)].SourceReference;
    public MacroAssembler.ProcessResult GetTemplate(string path) => Templates[FixFilename(path)].Template;
    public TemplateReference Get(string path) => Templates[FixFilename(path)];

    private static string FixFilename(string path)
    {
#if OS_WINDOWS
        return char.ToLower(path[0]) + path[1..];
#endif
#if OS_LINUX
        return path;
#endif
    }
}

internal record class TemplateReference(int SourceReference, MacroAssembler.ProcessResult Template);

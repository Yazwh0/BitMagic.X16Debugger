using BigMagic.TemplateEngine.Compiler;
using BitMagic.Common;

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
        Templates.Add(path.FixFilename(), new TemplateReference(id, templateResult));

        return id;
    }

    public int GetSourceReference(string path) => Templates[path.FixFilename()].SourceReference;
    public MacroAssembler.ProcessResult GetTemplate(string path) => Templates[path.FixFilename()].Template;
    public TemplateReference Get(string path) => Templates[path.FixFilename()];
}

internal record class TemplateReference(int SourceReference, MacroAssembler.ProcessResult Template);

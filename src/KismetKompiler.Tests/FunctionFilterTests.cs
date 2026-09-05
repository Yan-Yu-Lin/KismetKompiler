using KismetKompiler.Library.Compiler;
using KismetKompiler.Library.Compiler.Processing;
using KismetKompiler.Library.Parser;

namespace KismetKompiler.Tests;

[TestClass]
public sealed class FunctionFilterTests
{
    private static CompiledScriptContext Compile(string function)
    {
        var unit = new KismetScriptASTParser().Parse("""
            class TestClass {
                void UntouchedUnsupportedBody() { EX_SetMap(); }
                void Selected() { return; }
            }
            """);
        new TypeResolver().ResolveTypes(unit);
        return new KismetScriptCompiler { FunctionFilter = function }.CompileCompilationUnit(unit);
    }

    [TestMethod]
    public void SelectedFunctionSkipsUnsupportedOtherBody()
    {
        var script = Compile("Selected");
        Assert.AreEqual(1, script.Classes.Single().Functions.Count);
        Assert.AreEqual("Selected", script.Classes.Single().Functions.Single().Symbol.Name);
    }

    [TestMethod]
    public void MissingFunctionFailsInsteadOfProducingAnUnchangedAsset()
    {
        Assert.ThrowsException<ApplicationException>(() => Compile("Typo"));
    }
}

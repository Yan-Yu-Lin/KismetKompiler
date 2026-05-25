using KismetKompiler.Library.Compiler;
using KismetKompiler.Library.Compiler.Context;
using KismetKompiler.Library.Compiler.Processing;
using KismetKompiler.Library.Packaging;
using KismetKompiler.Library.Parser;
using KismetKompiler.Library.Syntax.Statements.Declarations;
using KismetKompiler.Library.Syntax.Statements.Expressions;
using KismetKompiler.Library.Syntax.Statements.Expressions.Identifiers;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace KismetKompiler.Tests;

[TestClass]
public sealed class ImportIdentityTests
{
    [TestMethod]
    public void PackageQualifiedImportDeclarationsDoNotCollide()
    {
        const string source = """
            [Import("/Script/UMG")]
            public class Function {}

            [Import("/Script/Engine")]
            public class Function {}

            class TestClass {
                sealed void Noop() {
                    return;
                }
            }
            """;

        var compilationUnit = new KismetScriptASTParser().Parse(source);
        new TypeResolver().ResolveTypes(compilationUnit);

        var compiler = new KismetScriptCompiler();
        compiler.CompileCompilationUnit(compilationUnit);
    }

    [TestMethod]
    public void EnsureObjectImportedUsesOuterInImportIdentity()
    {
        var linker = new TestUAssetLinker();

        var packageA = linker.AddPackage("/Script/A");
        var packageB = linker.AddPackage("/Script/B");

        var functionA = linker.AddObject(packageA, "Function", "Class");
        var functionB = linker.AddObject(packageB, "Function", "Class");

        Assert.AreNotEqual(functionA.Index, functionB.Index);
        Assert.AreEqual(2, linker.Asset.Imports.Count(x => x.ObjectName.ToString() == "Function"));
    }

    [TestMethod]
    public void CreatedFunctionImportsUseCoreUObjectClassPackage()
    {
        var linker = new TestUAssetLinker();
        var procedure = CreateExternalProcedureSymbol(
            "/Script/FSD",
            "GameFunctionLibrary",
            "GetFSDSaveGame");

        var procedureIndex = linker.AddProcedure(procedure);
        var import = procedureIndex.ToImport(linker.Asset);

        Assert.AreEqual("/Script/CoreUObject", import.ClassPackage.ToString());
        Assert.AreEqual("Function", import.ClassName.ToString());
        Assert.AreEqual("GetFSDSaveGame", import.ObjectName.ToString());
        Assert.AreEqual("GameFunctionLibrary", import.OuterIndex.ToImport(linker.Asset).ObjectName.ToString());
    }

    private static ProcedureSymbol CreateExternalProcedureSymbol(
        string packageName,
        string className,
        string procedureName)
    {
        var packageSymbol = new PackageSymbol()
        {
            Name = packageName,
            IsExternal = true,
            DeclaringSymbol = null,
        };

        var classSymbol = new ClassSymbol(new ClassDeclaration()
        {
            Identifier = new Identifier(className)
        })
        {
            Name = className,
            IsExternal = true,
            DeclaringSymbol = packageSymbol,
        };

        return new ProcedureSymbol(new ProcedureDeclaration()
        {
            Identifier = new Identifier(procedureName),
            ReturnType = TypeIdentifier.Void,
            Parameters = new(),
        })
        {
            Name = procedureName,
            IsExternal = true,
            DeclaringSymbol = classSymbol,
        };
    }

    private sealed class TestUAssetLinker : UAssetLinker
    {
        public UAsset Asset => Build();

        public FPackageIndex AddPackage(string objectName)
            => EnsurePackageImported(objectName);

        public FPackageIndex AddObject(FPackageIndex parent, string objectName, string className)
            => EnsureObjectImported(parent, objectName, className);

        public FPackageIndex AddProcedure(ProcedureSymbol symbol)
            => CreateProcedureImport(symbol);
    }
}

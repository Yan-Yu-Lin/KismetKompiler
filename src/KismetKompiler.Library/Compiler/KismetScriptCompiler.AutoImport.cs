using KismetKompiler.Library.Compiler.Context;
using KismetKompiler.Library.Syntax;
using KismetKompiler.Library.Syntax.Statements;
using KismetKompiler.Library.Syntax.Statements.Declarations;
using KismetKompiler.Library.Syntax.Statements.Expressions;
using KismetKompiler.Library.Syntax.Statements.Expressions.Binary;
using KismetKompiler.Library.Syntax.Statements.Expressions.Identifiers;
using KismetKompiler.Library.Syntax.Statements.Expressions.Unary;
using UAssetAPI.UnrealTypes;

namespace KismetKompiler.Library.Compiler;

public partial class KismetScriptCompiler
{
    private sealed record AutoImportClassSpec(
        string PackageName,
        string ClassName,
        bool IsStatic = false,
        string ImportClassName = "Class");

    private sealed record AutoImportFunctionSpec(
        string PackageName,
        string ClassName,
        string FunctionName,
        FunctionCustomFlags CustomFlags,
        bool IsStatic = false);

    private static readonly AutoImportClassSpec[] BuiltInAutoImportClasses =
    [
        new("/Script/Engine", "KismetMathLibrary", IsStatic: true),
        new("/Script/Engine", "KismetArrayLibrary", IsStatic: true),
        new("/Script/Engine", "KismetStringLibrary", IsStatic: true),
        new("/Script/Engine", "KismetTextLibrary", IsStatic: true),
        new("/Script/UMG", "WidgetBlueprintLibrary", IsStatic: true),
        new("/Script/FSD", "GameFunctionLibrary", IsStatic: true),
        new("/Script/FSD", "FSDSaveGame"),
        new("/Script/FSD", "CharacterSave", ImportClassName: "ScriptStruct"),
    ];

    private static readonly AutoImportFunctionSpec[] BuiltInAutoImportFunctions =
    [
        new("/Script/Engine", "KismetMathLibrary", "Multiply_IntInt", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.MathFunction, IsStatic: true),
        new("/Script/Engine", "KismetMathLibrary", "Subtract_IntInt", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.MathFunction, IsStatic: true),
        new("/Script/Engine", "KismetMathLibrary", "Add_IntInt", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.MathFunction, IsStatic: true),
        new("/Script/Engine", "KismetMathLibrary", "Divide_IntInt", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.MathFunction, IsStatic: true),
        new("/Script/Engine", "KismetMathLibrary", "Abs", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.MathFunction, IsStatic: true),
        new("/Script/Engine", "KismetMathLibrary", "Abs_Int", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.MathFunction, IsStatic: true),
        new("/Script/Engine", "KismetMathLibrary", "Less_IntInt", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.MathFunction, IsStatic: true),
        new("/Script/Engine", "KismetMathLibrary", "Greater_IntInt", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.MathFunction, IsStatic: true),

        new("/Script/Engine", "KismetArrayLibrary", "Array_Get", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.FinalFunction),
        new("/Script/Engine", "KismetArrayLibrary", "Array_Add", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.FinalFunction),
        new("/Script/Engine", "KismetArrayLibrary", "Array_Length", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.FinalFunction),

        new("/Script/Engine", "KismetStringLibrary", "Conv_StringToInt", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.MathFunction, IsStatic: true),
        new("/Script/Engine", "KismetStringLibrary", "Conv_IntToString", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.MathFunction, IsStatic: true),
        new("/Script/Engine", "KismetStringLibrary", "Concat_StrStr", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.MathFunction, IsStatic: true),

        new("/Script/Engine", "KismetTextLibrary", "Conv_TextToString", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.MathFunction, IsStatic: true),
        new("/Script/Engine", "KismetTextLibrary", "Conv_StringToText", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.MathFunction, IsStatic: true),

        new("/Script/UMG", "WidgetBlueprintLibrary", "Create", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.FinalFunction, IsStatic: true),

        new("/Script/FSD", "GameFunctionLibrary", "GetFSDSaveGame", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.MathFunction, IsStatic: true),
        new("/Script/FSD", "FSDSaveGame", "SaveToDisk", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.FinalFunction),
        new("/Script/FSD", "FSDSaveGame", "GetPlayerRank", FunctionCustomFlags.UnknownSignature | FunctionCustomFlags.FinalFunction),
    ];

    private static readonly IReadOnlyDictionary<string, AutoImportClassSpec> BuiltInAutoImportClassesByName =
        BuiltInAutoImportClasses.ToDictionary(x => x.ClassName);

    private static readonly IReadOnlyDictionary<(string ClassName, string FunctionName), AutoImportFunctionSpec> BuiltInAutoImportFunctionsByQualifiedName =
        BuiltInAutoImportFunctions.ToDictionary(x => (x.ClassName, x.FunctionName));

    private static readonly IReadOnlyDictionary<string, AutoImportFunctionSpec[]> BuiltInAutoImportFunctionsByClassName =
        BuiltInAutoImportFunctions
            .GroupBy(x => x.ClassName)
            .ToDictionary(x => x.Key, x => x.ToArray());

    private readonly Stack<Dictionary<string, string>> _autoImportVariableTypes = new();

    private IEnumerable<AutoImportFunctionSpec> GetAutoImportFunctionsForClass(string className)
    {
        if (BuiltInAutoImportFunctionsByClassName.TryGetValue(className, out var builtInFunctions))
        {
            foreach (var function in builtInFunctions)
                yield return function;
        }

        var manifestClass = AutoImportManifest?.Classes.FirstOrDefault(x => x.Name == className);
        if (manifestClass == null)
            yield break;

        foreach (var function in manifestClass.Functions)
        {
            yield return new AutoImportFunctionSpec(
                manifestClass.Package,
                manifestClass.Name,
                function.Name,
                ParseFunctionCustomFlags(function.CustomFlags),
                function.IsStatic || manifestClass.IsStatic);
        }
    }

    private bool TryGetAutoImportClass(string className, out AutoImportClassSpec spec)
    {
        if (BuiltInAutoImportClassesByName.TryGetValue(className, out spec))
            return true;

        var manifestClass = AutoImportManifest?.Classes.FirstOrDefault(x => x.Name == className);
        if (manifestClass == null)
        {
            spec = null;
            return false;
        }

        spec = new AutoImportClassSpec(
            manifestClass.Package,
            manifestClass.Name,
            manifestClass.IsStatic,
            manifestClass.ImportClassName);
        return true;
    }

    private bool TryGetAutoImportFunction(string className, string functionName, out AutoImportFunctionSpec spec)
    {
        if (BuiltInAutoImportFunctionsByQualifiedName.TryGetValue((className, functionName), out spec))
            return true;

        var manifestClass = AutoImportManifest?.Classes.FirstOrDefault(x => x.Name == className);
        var manifestFunction = manifestClass?.Functions.FirstOrDefault(x => x.Name == functionName);
        if (manifestClass == null || manifestFunction == null)
        {
            spec = null;
            return false;
        }

        spec = new AutoImportFunctionSpec(
            manifestClass.Package,
            manifestClass.Name,
            manifestFunction.Name,
            ParseFunctionCustomFlags(manifestFunction.CustomFlags),
            manifestFunction.IsStatic || manifestClass.IsStatic);
        return true;
    }

    private static FunctionCustomFlags ParseFunctionCustomFlags(string flags)
    {
        FunctionCustomFlags result = FunctionCustomFlags.UnknownSignature;
        foreach (var item in flags.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<FunctionCustomFlags>(item, out var flag))
                result |= flag;
        }

        return result;
    }

    private void ApplyAutoImports(CompilationUnit compilationUnit)
    {
        if (!AutoImportEnabled)
            return;

        foreach (var declaration in compilationUnit.Declarations)
        {
            if (declaration.Attributes.Any(IsPackageImportAttribute))
                continue;

            ScanDeclarationForAutoImports(declaration);
        }
    }

    private void ScanDeclarationForAutoImports(Declaration declaration)
    {
        switch (declaration)
        {
            case ClassDeclaration classDeclaration:
                foreach (var baseClass in classDeclaration.InheritedTypeIdentifiers)
                    EnsureAutoImportedClass(baseClass.Text, includeKnownFunctions: false);
                foreach (var child in classDeclaration.Declarations)
                    ScanDeclarationForAutoImports(child);
                break;
            case VariableDeclaration variableDeclaration:
                ScanTypeForAutoImports(variableDeclaration.Type);
                var variableTypeName = GetAutoImportTypeName(variableDeclaration.Type);
                if (variableTypeName != null && _autoImportVariableTypes.TryPeek(out var variableTypes))
                    variableTypes[variableDeclaration.Identifier.Text] = variableTypeName;
                if (variableDeclaration.Initializer != null)
                    ScanExpressionForAutoImports(variableDeclaration.Initializer);
                break;
            case ProcedureDeclaration procedureDeclaration:
                ScanTypeForAutoImports(procedureDeclaration.ReturnType);
                _autoImportVariableTypes.Push(new Dictionary<string, string>());
                foreach (var parameter in procedureDeclaration.Parameters)
                {
                    ScanTypeForAutoImports(parameter.Type);
                    var parameterTypeName = GetAutoImportTypeName(parameter.Type);
                    if (parameterTypeName != null)
                        _autoImportVariableTypes.Peek()[parameter.Identifier.Text] = parameterTypeName;
                }
                try
                {
                    if (procedureDeclaration.Body != null)
                        ScanCompoundStatementForAutoImports(procedureDeclaration.Body);
                }
                finally
                {
                    _autoImportVariableTypes.Pop();
                }
                break;
        }
    }

    private void ScanCompoundStatementForAutoImports(CompoundStatement compoundStatement)
    {
        foreach (var statement in compoundStatement)
            ScanStatementForAutoImports(statement);
    }

    private void ScanStatementForAutoImports(Statement statement)
    {
        switch (statement)
        {
            case Declaration declaration:
                ScanDeclarationForAutoImports(declaration);
                break;
            case Expression expression:
                ScanExpressionForAutoImports(expression);
                break;
            case ReturnStatement returnStatement:
                if (returnStatement.Value != null)
                    ScanExpressionForAutoImports(returnStatement.Value);
                break;
            case IfStatement ifStatement:
                ScanExpressionForAutoImports(ifStatement.Condition);
                ScanCompoundStatementForAutoImports(ifStatement.Body);
                if (ifStatement.ElseBody != null)
                    ScanCompoundStatementForAutoImports(ifStatement.ElseBody);
                break;
            case ForStatement forStatement:
                ScanStatementForAutoImports(forStatement.Initializer);
                ScanExpressionForAutoImports(forStatement.Condition);
                ScanExpressionForAutoImports(forStatement.AfterLoop);
                ScanCompoundStatementForAutoImports(forStatement.Body);
                break;
            case WhileStatement whileStatement:
                ScanExpressionForAutoImports(whileStatement.Condition);
                ScanCompoundStatementForAutoImports(whileStatement.Body);
                break;
            case SwitchStatement switchStatement:
                ScanExpressionForAutoImports(switchStatement.SwitchOn);
                foreach (var label in switchStatement.Labels)
                {
                    if (label is ConditionSwitchLabel conditionLabel)
                        ScanExpressionForAutoImports(conditionLabel.Condition);
                    foreach (var child in label.Body)
                        ScanStatementForAutoImports(child);
                }
                break;
        }
    }

    private void ScanExpressionForAutoImports(Expression expression)
    {
        switch (expression)
        {
            case MemberExpression { Context: Identifier contextIdentifier, Member: CallOperator callOperator }:
                if (TryGetAutoImportFunction(contextIdentifier.Text, callOperator.Identifier.Text, out _))
                {
                    EnsureAutoImportedFunction(contextIdentifier.Text, callOperator.Identifier.Text);
                }
                else if (_autoImportVariableTypes.TryPeek(out var variableTypes) &&
                         variableTypes.TryGetValue(contextIdentifier.Text, out var contextTypeName))
                {
                    EnsureAutoImportedFunction(contextTypeName, callOperator.Identifier.Text);
                }
                foreach (var argument in callOperator.Arguments)
                    ScanExpressionForAutoImports(argument.Expression);
                break;
            case MemberExpression memberExpression:
                ScanExpressionForAutoImports(memberExpression.Context);
                ScanExpressionForAutoImports(memberExpression.Member);
                break;
            case CallOperator callOperator:
                foreach (var argument in callOperator.Arguments)
                    ScanExpressionForAutoImports(argument.Expression);
                break;
            case InitializerList initializerList:
                foreach (var item in initializerList.Expressions)
                    ScanExpressionForAutoImports(item);
                break;
            case NewExpression newExpression:
                if (newExpression.TypeIdentifier != null)
                    ScanTypeForAutoImports(newExpression.TypeIdentifier);
                foreach (var item in newExpression.Initializer)
                    ScanExpressionForAutoImports(item);
                break;
            case SubscriptOperator subscriptOperator:
                ScanExpressionForAutoImports(subscriptOperator.Operand);
                ScanExpressionForAutoImports(subscriptOperator.Index);
                break;
            case CastOperator castOperator:
                ScanTypeForAutoImports(castOperator.TypeIdentifier);
                ScanExpressionForAutoImports(castOperator.Operand);
                break;
            case UnaryExpression unaryExpression:
                ScanExpressionForAutoImports(unaryExpression.Operand);
                break;
            case BinaryExpression binaryExpression:
                ScanExpressionForAutoImports(binaryExpression.Left);
                ScanExpressionForAutoImports(binaryExpression.Right);
                break;
            case ConditionalExpression conditionalExpression:
                ScanExpressionForAutoImports(conditionalExpression.Condition);
                ScanExpressionForAutoImports(conditionalExpression.ValueIfTrue);
                ScanExpressionForAutoImports(conditionalExpression.ValueIfFalse);
                break;
        }
    }

    private void ScanTypeForAutoImports(TypeIdentifier typeIdentifier)
    {
        if (typeIdentifier == null)
            return;

        if (typeIdentifier.IsConstructedType)
            ScanTypeForAutoImports(typeIdentifier.TypeParameter);
        else
            EnsureAutoImportedClass(typeIdentifier.Text, includeKnownFunctions: false);
    }

    private static string? GetAutoImportTypeName(TypeIdentifier typeIdentifier)
    {
        if (typeIdentifier == null)
            return null;
        if (typeIdentifier.IsConstructedType)
            return GetAutoImportTypeName(typeIdentifier.TypeParameter);
        return typeIdentifier.Text;
    }

    private void EnsureAutoImportedFunction(string className, string functionName)
    {
        if (!TryGetAutoImportFunction(className, functionName, out var spec))
            return;

        var classSymbol = EnsureAutoImportedClass(spec.ClassName, includeKnownFunctions: false);
        if (classSymbol == null || classSymbol.GetSymbol<ProcedureSymbol>(spec.FunctionName) != null)
            return;

        CreateAutoImportedFunction(classSymbol, spec);
    }

    private ClassSymbol? EnsureAutoImportedClass(string className, bool includeKnownFunctions)
    {
        if (!TryGetAutoImportClass(className, out var spec))
            return null;

        var packageSymbol = EnsureAutoImportedPackage(spec.PackageName);
        var classSymbol = packageSymbol.Members
            .OfType<ClassSymbol>()
            .FirstOrDefault(x => x.Name == spec.ClassName);
        var createdClass = false;

        if (classSymbol == null)
        {
            classSymbol = new ClassSymbol(new ClassDeclaration()
            {
                Identifier = new Identifier(spec.ClassName),
                Modifiers = spec.IsStatic ? ClassModifiers.Static : 0,
            })
            {
                DeclaringSymbol = packageSymbol,
                ImportClassName = spec.ImportClassName,
                IsExternal = true,
                Name = spec.ClassName,
            };
            createdClass = true;
        }

        if (!CurrentScope.SymbolExists(spec.ClassName, SymbolCategory.Class))
            DeclareSymbol(classSymbol);

        if (createdClass)
            DiagnosticSink?.Invoke($"Auto-import: {spec.PackageName}.{spec.ClassName}");

        if (includeKnownFunctions)
        {
            foreach (var functionSpec in GetAutoImportFunctionsForClass(spec.ClassName))
            {
                if (classSymbol.GetSymbol<ProcedureSymbol>(functionSpec.FunctionName) == null)
                    CreateAutoImportedFunction(classSymbol, functionSpec);
            }
        }

        return classSymbol;
    }

    private PackageSymbol EnsureAutoImportedPackage(string packageName)
    {
        var packageSymbol = CurrentScope.GetSymbol<PackageSymbol>(packageName);
        if (packageSymbol != null)
            return packageSymbol;

        packageSymbol = new PackageSymbol()
        {
            DeclaringSymbol = null,
            IsExternal = true,
            Name = packageName,
        };
        DeclareSymbol(packageSymbol);
        return packageSymbol;
    }

    private void CreateAutoImportedFunction(ClassSymbol classSymbol, AutoImportFunctionSpec spec)
    {
        var procedureSymbol = new ProcedureSymbol(new ProcedureDeclaration()
        {
            Attributes =
            [
                new AttributeDeclaration() { Identifier = new Identifier("UnknownSignature") },
            ],
            Identifier = new Identifier(spec.FunctionName),
            Modifiers = ProcedureModifier.Public |
                ProcedureModifier.Sealed |
                (spec.IsStatic ? ProcedureModifier.Static : 0),
            Parameters = new()
            {
                new Parameter()
                {
                    Type = new TypeIdentifier("Any"),
                    Identifier = new Identifier("args")
                }
            },
            ReturnType = TypeIdentifier.Void,
        })
        {
            CustomFlags = spec.CustomFlags,
            DeclaringSymbol = classSymbol,
            Flags = EFunctionFlags.FUNC_Public |
                EFunctionFlags.FUNC_Final |
                EFunctionFlags.FUNC_Native |
                (spec.IsStatic ? EFunctionFlags.FUNC_Static : 0),
            IsExternal = true,
            Name = spec.FunctionName,
        };

        DiagnosticSink?.Invoke($"Auto-import: {spec.PackageName}.{spec.ClassName}.{spec.FunctionName}");
    }
}

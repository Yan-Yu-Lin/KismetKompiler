using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using KismetKompiler.Library.Compiler;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

const string DefaultUnrealVersion = "4.27";
const string DefaultPakVersion = "V11";
const string DefaultMountPoint = "../../../";

try
{
    return await Run(args);
}
catch (CliError ex)
{
    Console.Error.WriteLine(ex.Message);
    return ex.ExitCode;
}

static async Task<int> Run(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        PrintHelp();
        return 0;
    }

    var command = args[0];
    var rest = args.Skip(1).ToArray();
    return command switch
    {
        "unpack" => await RunUnpack(rest),
        "decompile" => await RunDecompile(rest),
        "compile" => await RunCompile(rest),
        "pack" => await RunPack(rest),
        "validate" => RunValidate(rest),
        "generate-manifest" => RunGenerateManifest(rest),
        _ => throw new CliError($"Unknown command: {command}", 2),
    };
}

static async Task<int> RunUnpack(string[] args)
{
    var parsed = ParseArgs(args, requiredPositionals: 1);
    var output = parsed.GetOption("-o", "--output") ?? throw new CliError("unpack requires -o <dir>", 2);
    var repak = ToolLocator.Find("repak.exe", "DRG_MOD_REPAK", @"D:\dev\drg-modding-tools\repak.exe");

    return await RunProcess(repak, ["unpack", "-f", "-o", output, parsed.Positionals[0]]);
}

static async Task<int> RunDecompile(string[] args)
{
    var parsed = ParseArgs(args, requiredPositionals: 1);
    var output = parsed.GetOption("-o", "--output") ?? throw new CliError("decompile requires -o <kms>", 2);
    var kismet = ToolLocator.Find("KismetKompiler.exe", "DRG_MOD_KISMET");

    return await RunProcess(kismet, ["decompile", "-v", DefaultUnrealVersion, "-i", parsed.Positionals[0], "-o", output, "-f"]);
}

static async Task<int> RunCompile(string[] args)
{
    var parsed = ParseArgs(args, requiredPositionals: 1);
    var asset = parsed.GetOption("--asset") ?? throw new CliError("compile requires --asset <uasset>", 2);
    var output = parsed.GetOption("-o", "--output") ?? throw new CliError("compile requires -o <uasset>", 2);
    var kismet = ToolLocator.Find("KismetKompiler.exe", "DRG_MOD_KISMET");
    var normalizedKms = NormalizeKmsEncoding(parsed.Positionals[0]);

    try
    {
        var arguments = new List<string>
        {
            "compile", "-v", DefaultUnrealVersion,
            "-i", normalizedKms,
            "--asset", asset,
            "-o", output,
            "-f"
        };
        if (parsed.HasFlag("--no-auto-import"))
            arguments.Add("--no-auto-import");
        var manifest = parsed.GetOption("--auto-import-manifest");
        if (manifest != null)
        {
            arguments.Add("--auto-import-manifest");
            arguments.Add(manifest);
        }

        return await RunProcess(kismet, arguments);
    }
    finally
    {
        TryTrashTempFile(normalizedKms, parsed.Positionals[0]);
    }
}

static async Task<int> RunPack(string[] args)
{
    var parsed = ParseArgs(args, requiredPositionals: 1);
    var output = parsed.GetOption("-o", "--output") ?? throw new CliError("pack requires -o <pak>", 2);
    var repak = ToolLocator.Find("repak.exe", "DRG_MOD_REPAK", @"D:\dev\drg-modding-tools\repak.exe");

    return await RunProcess(repak, ["pack", "--version", DefaultPakVersion, "--mount-point", DefaultMountPoint, parsed.Positionals[0], output]);
}

static int RunValidate(string[] args)
{
    var parsed = ParseArgs(args, requiredPositionals: 1);
    var root = Path.GetFullPath(parsed.Positionals[0]);
    if (!Directory.Exists(root))
        throw new CliError($"Directory not found: {root}", 2);

    var errors = new List<string>();
    var uassets = Directory.EnumerateFiles(root, "*.uasset", SearchOption.AllDirectories).ToList();
    var uexps = Directory.EnumerateFiles(root, "*.uexp", SearchOption.AllDirectories).ToHashSet(StringComparer.OrdinalIgnoreCase);

    foreach (var uasset in uassets)
    {
        var uexp = Path.ChangeExtension(uasset, ".uexp");
        if (!uexps.Contains(uexp))
            errors.Add($"Missing .uexp for {Path.GetRelativePath(root, uasset)}");

        ValidateImportTable(root, uasset, errors);
    }

    foreach (var uexp in uexps)
    {
        var uasset = Path.ChangeExtension(uexp, ".uasset");
        if (!File.Exists(uasset))
            errors.Add($"Missing .uasset for {Path.GetRelativePath(root, uexp)}");
    }

    if (!Directory.Exists(Path.Combine(root, "FSD", "Content")) &&
        !Directory.Exists(Path.Combine(root, "Content")))
    {
        errors.Add("Mount layout does not look like a DRG pak root; expected FSD/Content or Content under the input directory.");
    }

    if (errors.Count == 0)
    {
        Console.WriteLine($"Validation succeeded: {uassets.Count} .uasset files checked.");
        return 0;
    }

    Console.Error.WriteLine("Validation failed:");
    foreach (var error in errors)
        Console.Error.WriteLine($"- {error}");
    return 1;
}

static int RunGenerateManifest(string[] args)
{
    var parsed = ParseArgs(args, requiredPositionals: 1);
    var output = parsed.GetOption("-o", "--output") ?? throw new CliError("generate-manifest requires -o <json>", 2);
    var manifest = HeaderDumpManifestGenerator.Generate(parsed.Positionals[0]);
    manifest.Save(output);
    Console.WriteLine($"Generated {output}: {manifest.Classes.Count} classes/structs, {manifest.Classes.Sum(x => x.Functions.Count)} functions.");
    return 0;
}

static void ValidateImportTable(string root, string uassetPath, List<string> errors)
{
    try
    {
        var asset = new UAsset(uassetPath, EngineVersion.VER_UE4_27);
        for (var i = 0; i < asset.Imports.Count; i++)
        {
            var import = asset.Imports[i];
            if (!import.OuterIndex.IsNull() && !import.OuterIndex.IsImport())
                errors.Add($"{Path.GetRelativePath(root, uassetPath)} import {i}: OuterIndex is not an import/null index ({import.OuterIndex.Index}).");
            if (import.OuterIndex.IsImport() && -import.OuterIndex.Index > asset.Imports.Count)
                errors.Add($"{Path.GetRelativePath(root, uassetPath)} import {i}: OuterIndex out of range ({import.OuterIndex.Index}).");
            if (string.IsNullOrWhiteSpace(import.ObjectName?.ToString()))
                errors.Add($"{Path.GetRelativePath(root, uassetPath)} import {i}: ObjectName is empty.");
        }
    }
    catch (Exception ex)
    {
        errors.Add($"{Path.GetRelativePath(root, uassetPath)}: failed to read import table: {ex.Message}");
    }
}

static string NormalizeKmsEncoding(string path)
{
    using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
    var text = reader.ReadToEnd();
    var tempPath = Path.Combine(Path.GetTempPath(), $"drg-mod-{Guid.NewGuid():N}.kms");
    File.WriteAllText(tempPath, text, Encoding.Unicode);
    return tempPath;
}

static void TryTrashTempFile(string tempPath, string originalPath)
{
    if (string.Equals(Path.GetFullPath(tempPath), Path.GetFullPath(originalPath), StringComparison.OrdinalIgnoreCase))
        return;

    try
    {
        File.Delete(tempPath);
    }
    catch
    {
        // Best effort cleanup for temporary encoding-normalized scripts.
    }
}

static async Task<int> RunProcess(string executable, IReadOnlyList<string> arguments)
{
    var startInfo = new ProcessStartInfo(executable)
    {
        UseShellExecute = false,
    };
    foreach (var argument in arguments)
        startInfo.ArgumentList.Add(argument);

    Console.WriteLine($"> {Path.GetFileName(executable)} {string.Join(" ", arguments.Select(QuoteForDisplay))}");
    using var process = Process.Start(startInfo) ?? throw new CliError($"Failed to start {executable}", 1);
    await process.WaitForExitAsync();
    return process.ExitCode;
}

static string QuoteForDisplay(string argument)
    => argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument;

static ParsedArgs ParseArgs(string[] args, int requiredPositionals)
{
    var positionals = new List<string>();
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (arg is "-o" or "--output" or "--asset" or "--auto-import-manifest")
        {
            if (i + 1 >= args.Length)
                throw new CliError($"{arg} requires a value", 2);
            options[arg] = args[++i];
        }
        else if (arg.StartsWith("--"))
        {
            flags.Add(arg);
        }
        else
        {
            positionals.Add(arg);
        }
    }

    if (positionals.Count < requiredPositionals)
        throw new CliError("Missing required positional argument.", 2);

    return new ParsedArgs(positionals, options, flags);
}

static void PrintHelp()
{
    Console.WriteLine("""
    drg-mod unpack <pak> -o <dir>
    drg-mod decompile <uasset> -o <kms>
    drg-mod compile <kms> --asset <uasset> -o <uasset> [--no-auto-import] [--auto-import-manifest <json>]
    drg-mod pack <dir> -o <pak>
    drg-mod validate <dir>
    drg-mod generate-manifest <FSD.hpp> -o <drg-symbols.json>

    Defaults: UE 4.27, pak V11, mount point ../../../
    """);
}

internal sealed record ParsedArgs(
    IReadOnlyList<string> Positionals,
    IReadOnlyDictionary<string, string> Options,
    IReadOnlySet<string> Flags)
{
    public string? GetOption(params string[] names)
        => names.Select(name => Options.TryGetValue(name, out var value) ? value : null).FirstOrDefault(value => value != null);

    public bool HasFlag(string name) => Flags.Contains(name);
}

internal static class ToolLocator
{
    public static string Find(string fileName, string envVar, string? fallback = null)
    {
        var envPath = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
            return envPath;

        var localPath = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(localPath))
            return localPath;

        if (!string.IsNullOrWhiteSpace(fallback) && File.Exists(fallback))
            return fallback;

        throw new CliError($"Could not locate {fileName}. Put it next to drg-mod.exe or set {envVar}.", 2);
    }
}

internal sealed class CliError(string message, int exitCode) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}

internal static partial class HeaderDumpManifestGenerator
{
    public static AutoImportManifest Generate(string headerPath)
    {
        if (!File.Exists(headerPath))
            throw new CliError($"Header file not found: {headerPath}", 2);

        var manifest = new AutoImportManifest()
        {
            Version = 1,
            Package = "/Script/FSD",
        };

        AutoImportManifestClass? currentClass = null;
        string? currentRawName = null;
        var currentBraceDepth = 0;

        foreach (var rawLine in File.ReadLines(headerPath))
        {
            var line = rawLine.Trim();
            if (currentClass == null)
            {
                var typeMatch = TypeDeclarationRegex().Match(line);
                if (!typeMatch.Success)
                    continue;

                var declarationKind = typeMatch.Groups[1].Value;
                currentRawName = typeMatch.Groups[2].Value;
                var baseName = typeMatch.Groups[3].Success ? typeMatch.Groups[3].Value : "";
                var isStruct = declarationKind == "struct";
                var isBlueprintFunctionLibrary = baseName == "UBlueprintFunctionLibrary";

                currentClass = new AutoImportManifestClass()
                {
                    Package = "/Script/FSD",
                    Name = StripUnrealPrefix(currentRawName),
                    ImportClassName = isStruct ? "ScriptStruct" : "Class",
                    IsStatic = isBlueprintFunctionLibrary,
                };
                currentBraceDepth = CountChar(line, '{') - CountChar(line, '}');
                if (currentBraceDepth <= 0 && line.EndsWith(";"))
                {
                    manifest.Classes.Add(currentClass);
                    currentClass = null;
                    currentRawName = null;
                }
                continue;
            }

            currentBraceDepth += CountChar(line, '{') - CountChar(line, '}');
            if (currentBraceDepth <= 0)
            {
                manifest.Classes.Add(currentClass);
                currentClass = null;
                currentRawName = null;
                currentBraceDepth = 0;
                continue;
            }

            var functionMatch = FunctionDeclarationRegex().Match(line);
            if (!functionMatch.Success)
                continue;

            var functionName = functionMatch.Groups[1].Value;
            if (functionName == currentRawName || functionName == StripUnrealPrefix(currentRawName))
                continue;

            currentClass.Functions.Add(new AutoImportManifestFunction()
            {
                Name = functionName,
                IsStatic = currentClass.IsStatic,
                CustomFlags = currentClass.IsStatic
                    ? "UnknownSignature|MathFunction"
                    : "UnknownSignature|FinalFunction",
            });
        }

        manifest.Classes = manifest.Classes
            .GroupBy(x => x.Name)
            .Select(group =>
            {
                var item = group.First();
                item.Functions = item.Functions
                    .GroupBy(x => x.Name)
                    .Select(x => x.First())
                    .OrderBy(x => x.Name)
                    .ToList();
                return item;
            })
            .OrderBy(x => x.Name)
            .ToList();

        return manifest;
    }

    private static string StripUnrealPrefix(string name)
    {
        if (name.Length > 1 && name[0] is 'U' or 'A' or 'F')
            return name[1..];
        return name;
    }

    private static int CountChar(string text, char value)
        => text.Count(x => x == value);

    [GeneratedRegex(@"^(struct|class)\s+([A-Za-z_]\w*)(?:\s*:\s*public\s+([A-Za-z_]\w*))?")]
    private static partial Regex TypeDeclarationRegex();

    [GeneratedRegex(@"^(?!if\b|for\b|while\b|switch\b|return\b)(?:[\w:<>,\s&*]+\s+)+([A-Za-z_]\w*)\s*\([^;{}]*\)\s*;")]
    private static partial Regex FunctionDeclarationRegex();
}

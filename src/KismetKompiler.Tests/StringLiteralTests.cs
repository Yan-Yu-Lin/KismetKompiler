using KismetKompiler.Library.Parser;
using KismetKompiler.Library.Utilities;

namespace KismetKompiler.Tests;

[TestClass]
public sealed class StringLiteralTests
{
    [DataTestMethod]
    [DataRow("")]
    [DataRow("Module \"Enemies\" is not installed")]
    [DataRow("\"")]
    [DataRow("C:\\mods\\new\\test")]
    [DataRow("literal \\n and real\nnewline\r\n\tend")]
    [DataRow("繁體中文 🪨")]
    [DataRow("\0\a\b\f\v")]
    public void EncodedStringsParseAndRoundTrip(string value)
    {
        var encoded = KismetStringLiteral.Encode(value);
        Assert.AreEqual(value, KismetStringLiteral.Decode(encoded));
        // Exercise the actual grammar/parser too, especially terminal quotes.
        new KismetScriptASTParser().Parse($"class Test {{ void Run() {{ string s; s = {encoded}; }} }}");
    }

    [DataTestMethod]
    [DataRow("\"\\65\\x42\"", "AB")]
    [DataRow("\"\\z \t\r\nx\"", "x")]
    [DataRow("\"a\\\r\nb\"", "a\nb")]
    public void GrammarEscapesDecode(string token, string expected)
    {
        Assert.AreEqual(expected, KismetStringLiteral.Decode(token));
    }
}

using System.Globalization;
using System.Text;

namespace KismetKompiler.Library.Utilities;

/// <summary>Encode/decode the string escapes accepted by KismetScript.g4.</summary>
public static class KismetStringLiteral
{
    public static string Encode(string value)
    {
        var result = new StringBuilder("\"");
        foreach (var c in value)
        {
            result.Append(c switch
            {
                '\\' => "\\\\", '"' => "\\\"", '\a' => "\\a", '\b' => "\\b",
                '\f' => "\\f", '\n' => "\\n", '\r' => "\\r", '\t' => "\\t",
                '\v' => "\\v",
                < ' ' => "\\x" + ((int)c).ToString("X2", CultureInfo.InvariantCulture),
                _ => c.ToString(),
            });
        }
        return result.Append('"').ToString();
    }

    public static string Decode(string token)
    {
        if (token.Length < 2 || token[0] != '"' || token[^1] != '"')
            throw new FormatException("Expected a quoted Kismet string literal.");
        var result = new StringBuilder();
        for (int i = 1; i < token.Length - 1; i++)
        {
            char c = token[i];
            if (c != '\\')
            {
                result.Append(c);
                continue;
            }
            if (++i >= token.Length - 1)
                throw new FormatException("Incomplete string escape.");
            c = token[i];
            switch (c)
            {
                case 'a': result.Append('\a'); break;
                case 'b': result.Append('\b'); break;
                case 'f': result.Append('\f'); break;
                case 'n': result.Append('\n'); break;
                case 'r': result.Append('\r'); break;
                case 't': result.Append('\t'); break;
                case 'v': result.Append('\v'); break;
                case '\\': case '"': case '\'': result.Append(c); break;
                case '\r':
                    if (i + 1 >= token.Length - 1 || token[++i] != '\n')
                        throw new FormatException("Expected escaped newline.");
                    result.Append('\n');
                    break;
                case '\n': result.Append('\n'); break;
                case 'z':
                    while (i + 1 < token.Length - 1 && char.IsWhiteSpace(token[i + 1])) i++;
                    break;
                case 'x':
                    if (i + 2 >= token.Length - 1)
                        throw new FormatException("Incomplete hex string escape.");
                    result.Append((char)int.Parse(token.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 2;
                    break;
                default:
                    if (c < '0' || c > '9')
                        throw new FormatException($"Unknown string escape: {c}");
                    int count = 1, value = c - '0';
                    // Grammar accepts 3 decimal digits only when the first is 0–2.
                    int maxDigits = c <= '2' ? 3 : 2;
                    while (count < maxDigits && i + 1 < token.Length - 1 &&
                           token[i + 1] >= '0' && token[i + 1] <= '9')
                    {
                        value = value * 10 + token[++i] - '0';
                        count++;
                    }
                    result.Append((char)value);
                    break;
            }
        }
        return result.ToString();
    }
}

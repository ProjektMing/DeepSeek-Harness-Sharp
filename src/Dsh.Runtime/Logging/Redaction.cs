using System.Text.RegularExpressions;

namespace Dsh.Runtime.Logging;

internal static partial class Redaction
{
    [GeneratedRegex(@"\bsk-[A-Za-z0-9_\-]{4,}", RegexOptions.None)]
    private static partial Regex ApiKeyPattern();

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9_\-\.=]{8,}", RegexOptions.None)]
    private static partial Regex BearerPattern();

    public static string Apply(string text)
    {
        if (text.Length == 0)
            return text;
        var redacted = ApiKeyPattern().Replace(text, "sk-***");
        return BearerPattern().Replace(redacted, "Bearer ***");
    }
}

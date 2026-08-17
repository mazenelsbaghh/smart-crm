using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Modules.Conversations.Services;

public static class EgyptianPhoneNumber
{
    private static readonly Regex PhonePattern = new(@"(?:20)?(1[0125]\d{8})\b", RegexOptions.Compiled);

    public static string? Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var normalized = NormalizeDigits(text);
        var compact = Regex.Replace(normalized, @"[\s\-\(\)\+]", string.Empty);
        var match = PhonePattern.Match(compact);
        return match.Success ? "20" + match.Groups[1].Value : null;
    }

    public static string NormalizeDigits(string text)
    {
        var normalized = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            var digit = CharUnicodeInfo.GetDecimalDigitValue(character);
            normalized.Append(digit >= 0 ? (char)('0' + digit) : character);
        }
        return normalized.ToString();
    }
}

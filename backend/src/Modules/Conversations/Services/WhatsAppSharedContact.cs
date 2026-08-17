using System.Text.RegularExpressions;

namespace Modules.Conversations.Services;

public sealed record WhatsAppSharedContact(string PhoneNumber, string? Name);

public static class WhatsAppSharedContactParser
{
    public static WhatsAppSharedContact? ExtractOwnContact(string? content)
    {
        if (string.IsNullOrWhiteSpace(content) || ClearlyReferencesAnotherPerson(content)) return null;
        var phoneNumber = EgyptianPhoneNumber.Extract(content);
        return phoneNumber == null ? null : new WhatsAppSharedContact(phoneNumber, ExtractName(content));
    }

    private static bool ClearlyReferencesAnotherPerson(string content) => Regex.IsMatch(
        content,
        @"رقم(?:ه|ها|هم)\b|(?:صاحبي|صاحبتي|اخويا|اختي|ابني|بنتي|زوجي|مراتي|والدي|والدتي)\b|(?:عايز|عاوزه|عاوز)\s+احجز\s+ل",
        RegexOptions.IgnoreCase);

    private static string? ExtractName(string content)
    {
        var normalized = EgyptianPhoneNumber.NormalizeDigits(content);
        var separateNameLine = normalized
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => EgyptianPhoneNumber.Extract(line) == null && Regex.IsMatch(line, @"\p{L}"));
        if (separateNameLine != null) return NormalizeName(separateNameLine);

        var labelledName = Regex.Match(
            normalized,
            @"(?:الاسم|اسمي)\s*[:\-]?\s*(?<name>[\p{L}\s]{2,80}?)(?=(?:\+?20|0)?1[0125])",
            RegexOptions.IgnoreCase);
        return labelledName.Success ? NormalizeName(labelledName.Groups["name"].Value) : null;
    }

    private static string? NormalizeName(string name)
    {
        var candidate = Regex.Replace(name, @"[^\p{L}\s]", " ");
        candidate = Regex.Replace(candidate, @"\s+", " ").Trim();
        var wordCount = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return wordCount is >= 2 and <= 5 && candidate.Length <= 80 ? candidate : null;
    }
}

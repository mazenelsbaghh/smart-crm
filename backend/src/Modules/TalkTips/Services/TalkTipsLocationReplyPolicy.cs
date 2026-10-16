using System.Text.RegularExpressions;
using Modules.AI.Services;
using Modules.Projects.Domain;

namespace Modules.TalkTips.Services;

public static class TalkTipsLocationReplyPolicy
{
    public const string Instructions = """
        TALKTIPS CENTER LOCATION:
        The physical center is in Alexandria, Egypt. Every customer-facing center address or map message must explicitly include الإسكندرية in the same message paragraph, together with the exact approved address and map link. Preserve the street, building, floor and landmarks from approved knowledge; never invent or alter them or claim another city's branch.
        For offline attendance, confirm that EACH attendee is in Alexandria and can attend the center. If the attendee's city is unknown, ask directly: حضرتك مقيم في الإسكندرية وتقدر تحضر السنتر هناك؟ Do not suggest or claim a confirmed offline booking before that confirmation. Never infer a customer's or friend's city from the center's address, an offline preference, or another person's city. Use an already explicit customer confirmation without repeatedly asking.
        Online bookings remain online and do not require Alexandria residency. An address inquiry alone is not a booking request and must still receive the approved address.
        """;

    private static readonly Regex MapLink = new(
        @"https?://(?:maps\.app\.goo\.gl/|maps\.google\.[a-z.]+/|(?:www\.)?google\.[a-z.]+/maps(?:/|\?|$)|goo\.gl/maps/)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex CenterAddress = new(
        @"عنواننا|(?:عنوان|مكان|موقع|لوكيشن)\s+(?:السنتر|المركز|المقر|الفرع|الاكاديميه)|(?:السنتر|المركز|مقرنا)\s+(?:في|موجود)\b|\bcenter address\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex AddressHeading = new(
        @"\bالعنوان\s*(?:تفصيليا\s*)?[:：]",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex PhysicalAddressDetails = new(
        @"\b(?:شارع|عماره|الدور|street|building|floor)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex CustomerAddress = new(
        @"(?:عنوان|لوكيشن|موقع)(?:ك|كم|ي)|(?:عنوان|لوكيشن|موقع)\s+(?:حضرتك|العميل|بيتك|منزلك|البيت|المنزل)|your\s+(?:home\s+)?(?:address|location|map)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex CustomerSharedLocation = new(
        @"(?:لوكيشن|موقع|عنوان|رابط).{0,40}(?:حضرتك|انت|انتي)\s+(?:بعت|ارسل|شارك)|(?:حضرتك|انت|انتي)\s+(?:بعت|ارسل|شارك).{0,40}(?:لوكيشن|موقع|عنوان|رابط)|(?:لوكيشن|موقع|عنوان|رابط).{0,30}(?:بعت|ارسل|شارك).{0,15}حضرتك|(?:location|map|link|address).{0,30}you\s+(?:sent|shared)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex CustomerResidence = new(
        @"(?:ساكن|مقيم|تسكن|بتسكن)\s+(?:في|شارع)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Alexandria = new(
        @"(?:ال)?اسكندري[ها]|\balexandria\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static void Apply(ProjectSettings settings, MarketingAnalysisResult analysis)
    {
        if (!settings.IsTalkTipsTrialGateEnabled) return;
        analysis.ReplyContent = IncludeAlexandria(analysis.ReplyContent);
        if (analysis.PublicCommentReply is not null)
            analysis.PublicCommentReply = IncludeAlexandria(analysis.PublicCommentReply);
    }

    public static string IncludeAlexandria(string reply)
    {
        var paragraphs = Regex.Split(reply, @"(\r?\n\s*\r?\n)");
        for (var index = 0; index < paragraphs.Length; index += 2)
        {
            var customerMap = index >= 2 && IsStandaloneMapLink(paragraphs[index])
                && RefersToCustomerLocation(Normalize(paragraphs[index - 2]));
            if (!customerMap && NeedsCenterCity(paragraphs[index]))
                paragraphs[index] = "الإسكندرية — " + paragraphs[index];
        }
        return string.Concat(paragraphs);
    }

    private static bool NeedsCenterCity(string paragraph)
    {
        var normalized = Normalize(paragraph);
        if (Alexandria.IsMatch(normalized) || RefersToCustomerLocation(normalized)) return false;
        if (CenterAddress.IsMatch(normalized)
            || AddressHeading.IsMatch(normalized) && PhysicalAddressDetails.IsMatch(normalized)) return true;
        return MapLink.IsMatch(paragraph) && !CustomerResidence.IsMatch(normalized);
    }

    private static bool RefersToCustomerLocation(string normalized) =>
        CustomerAddress.IsMatch(normalized) || CustomerSharedLocation.IsMatch(normalized);

    private static bool IsStandaloneMapLink(string paragraph)
    {
        var link = paragraph.Trim();
        return !link.Any(char.IsWhiteSpace) && Uri.TryCreate(link, UriKind.Absolute, out _)
            && MapLink.IsMatch(link);
    }

    private static string Normalize(string text) => Regex.Replace(text.ToLowerInvariant(),
        @"[\u064B-\u065F\u0670\u0640]", string.Empty)
        .Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ى', 'ي').Replace('ة', 'ه');
}

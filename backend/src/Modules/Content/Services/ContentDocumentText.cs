using System.Text.RegularExpressions;

namespace Modules.Content.Services;

internal static partial class ContentDocumentText
{
    private const string TitleLabel = @"(?:title|sub[\t ]?title|heading|تايتل|(?:ال)?عنوان(?:[\t ]+(?:ال)?فرعي)?)";
    private const string Separator = @"(?:[\t ]*[:：]|[\t ]+[-–—][\t ]+)";
    private const string LabelPrefix = @"[\t ]*(?:#{1,6}[\t ]+)?(?:\*\*" + TitleLabel + Separator
        + @"\*\*|__" + TitleLabel + Separator + @"__|\*\*" + TitleLabel + @"\*\*" + Separator
        + @"|__" + TitleLabel + @"__" + Separator + "|" + TitleLabel + Separator + @")[\t ]*";

    // A delimiter distinguishes authoring labels from meaningful phrases such as "Title insurance".
    // Repeated prefixes are consumed together so preview and image generation use identical copy.
    [GeneratedRegex("^(?:" + LabelPrefix + ")+", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex TitleLabels();

    internal static string WithoutTitleLabels(string text) => TitleLabels().Replace(text, string.Empty);

    [GeneratedRegex(@"(?<![\p{L}\p{N}_/])(?:\*\*|__)?slide(?:r)?s?[\t ]*\#?[\t ]*\p{Nd}+(?![\p{L}\p{N}_])(?:\uFE0F?\u20E3)?(?:[\t ]*[:：.\-–—])?(?:\*\*|__)?(?:[\t ]*[:：\-–—])?[\t ]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex SlideLabels();

    internal static string ForDisplay(string text)
    {
        var cleaned = ContentDocumentEmoji.Remove(SlideLabels().Replace(text, string.Empty));
        string previous;
        do
        {
            previous = cleaned;
            cleaned = WithoutTitleLabels(SlideLabels().Replace(cleaned, string.Empty));
        } while (cleaned != previous);
        return cleaned;
    }
}

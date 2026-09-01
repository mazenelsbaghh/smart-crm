using System.Text;
using System.Text.RegularExpressions;

namespace Modules.Content.Services;

internal static class ContentCaptionOriginality
{
    private const int MinimumCaptionWords = 35;
    private const int MaximumCaptionWords = 110;
    private const int KnowledgeCopySequenceWords = 7;
    private const int HistoricalCopySequenceWords = 6;
    private const int PlanCopySequenceWords = 5;

    private static readonly HashSet<string> ComparisonStopWords = new(StringComparer.Ordinal)
    {
        "اللي", "الي", "علي", "عن", "في", "من", "مع", "او", "ان", "كان", "كل",
        "ده", "دي", "هو", "هي", "هما", "احنا", "انت", "انتي", "عشان", "لكن", "بس", "مش",
        "اي", "ايه", "لو", "لما", "ما", "ولا", "و", "ف", "ب", "ك", "ل"
    };

    internal static void EnsureStandaloneCopy(
        GeneratedCopy copy,
        IReadOnlyList<KnowledgeSource> knowledge,
        IReadOnlyList<HistoricalContent> history)
    {
        EnsurePublishableLength(copy.Caption);
        EnsureNotCopiedFromKnowledge(copy.Caption, knowledge);
        EnsureNotRepeatedFromHistory(copy.Caption, history);
    }

    internal static void EnsureWeeklyPlan(
        IReadOnlyList<GeneratedCopy> generatedPlan,
        IReadOnlyList<KnowledgeSource> knowledge,
        IReadOnlyList<HistoricalContent> history)
    {
        foreach (var copy in generatedPlan)
            EnsureStandaloneCopy(copy, knowledge, history);

        EnsureCaptionsDiffer(generatedPlan);
        EnsureCallToActionDiversity(generatedPlan);
    }

    internal static string HistoryExcerpt(string caption)
    {
        var cleaned = Regex.Replace(caption, @"\s+", " ").Trim();
        return cleaned.Length <= 180 ? cleaned : $"{cleaned[..177]}...";
    }

    internal static string NormalizeForComparison(string value)
    {
        var withoutLinksAndTags = Regex.Replace(
            value,
            @"https?://\S+|#[\p{L}\p{N}_]+",
            " ",
            RegexOptions.IgnoreCase);
        var normalizedCharacters = withoutLinksAndTags.Normalize(NormalizationForm.FormD)
            .Where(character => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(NormalizeCharacter)
            .ToArray();
        return string.Join(' ', new string(normalizedCharacters)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static void EnsureCaptionsDiffer(IReadOnlyList<GeneratedCopy> generatedPlan)
    {
        for (var firstIndex = 0; firstIndex < generatedPlan.Count; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < generatedPlan.Count; secondIndex++)
            {
                var firstCaption = generatedPlan[firstIndex].Caption;
                var secondCaption = generatedPlan[secondIndex].Caption;
                if (HasSharedSequence(firstCaption, secondCaption, PlanCopySequenceWords)
                    || Similarity(firstCaption, secondCaption) >= 0.42)
                {
                    throw new InvalidOperationException(
                        $"كابشنا اليومين {firstIndex + 1} و{secondIndex + 1} متشابهان أكثر من اللازم.");
                }
            }
        }
    }

    private static void EnsurePublishableLength(string caption)
    {
        var wordCount = Tokenize(caption).Length;
        if (wordCount is < MinimumCaptionWords or > MaximumCaptionWords)
        {
            throw new InvalidOperationException(
                $"الكابشن يجب أن يكون بين {MinimumCaptionWords} و{MaximumCaptionWords} كلمة؛ الحالي {wordCount} كلمة.");
        }
    }

    private static void EnsureNotCopiedFromKnowledge(
        string caption,
        IReadOnlyList<KnowledgeSource> knowledge)
    {
        if (knowledge.Any(source => HasSharedSequence(caption, source.Content, KnowledgeCopySequenceWords)))
        {
            throw new InvalidOperationException(
                "الكابشن نقل جملة من قاعدة المعرفة؛ استخدم الحقيقة فقط وأعد بناء الفكرة والصياغة من الصفر.");
        }
    }

    private static void EnsureNotRepeatedFromHistory(
        string caption,
        IReadOnlyList<HistoricalContent> history)
    {
        if (history.Any(previous => !string.IsNullOrWhiteSpace(previous.Caption)
                && (HasSharedSequence(caption, previous.Caption, HistoricalCopySequenceWords)
                    || Similarity(caption, previous.Caption) >= 0.55)))
        {
            throw new InvalidOperationException(
                "الكابشن أعاد صياغة كابشن قديم بشكل قريب؛ غيّر الفكرة والبناء والافتتاحية والـCTA.");
        }
    }

    private static void EnsureCallToActionDiversity(IReadOnlyList<GeneratedCopy> generatedPlan)
    {
        var callToActions = generatedPlan
            .Select(copy => MeaningfulTokens(ExtractCallToAction(copy.Caption)).ToHashSet(StringComparer.Ordinal))
            .ToArray();

        EnsureCallToActionsDiffer(callToActions);
        if (CountFrequentlyRepeatedWords(callToActions) >= 2)
            throw new InvalidOperationException("معظم الكابشنات تنتهي بنفس الرسالة؛ نوّع بين الحوار والحفظ والمشاركة والتجربة والبيع المباشر.");
    }

    private static void EnsureCallToActionsDiffer(IReadOnlyList<HashSet<string>> callToActions)
    {
        for (var firstIndex = 0; firstIndex < callToActions.Count; firstIndex++)
        {
            for (var secondIndex = firstIndex + 1; secondIndex < callToActions.Count; secondIndex++)
            {
                if (Jaccard(callToActions[firstIndex], callToActions[secondIndex]) >= 0.55)
                    throw new InvalidOperationException("الخطة كررت نفس نهاية الكابشن أو الدعوة لاتخاذ إجراء.");
            }
        }
    }

    private static int CountFrequentlyRepeatedWords(IEnumerable<HashSet<string>> callToActions) =>
        callToActions
            .SelectMany(tokens => tokens)
            .GroupBy(token => token, StringComparer.Ordinal)
            .Where(group => group.Count() >= 4)
            .Take(2)
            .Count();

    private static string ExtractCallToAction(string caption)
    {
        var withoutLinksAndTags = Regex.Replace(
            caption,
            @"https?://\S+|#[\p{L}\p{N}_]+",
            " ",
            RegexOptions.IgnoreCase);
        var segments = Regex.Split(withoutLinksAndTags, @"(?:\r?\n){2,}|(?<=[.!؟!])\s+")
            .Select(segment => segment.Trim())
            .Where(segment => Tokenize(segment).Length >= 3)
            .ToArray();
        return segments.LastOrDefault() ?? withoutLinksAndTags;
    }

    private static bool HasSharedSequence(string first, string second, int sequenceLength)
    {
        var firstTokens = Tokenize(first);
        var secondTokens = Tokenize(second);
        if (firstTokens.Length < sequenceLength || secondTokens.Length < sequenceLength) return false;

        var firstSequences = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index <= firstTokens.Length - sequenceLength; index++)
            firstSequences.Add(string.Join(' ', firstTokens, index, sequenceLength));
        for (var index = 0; index <= secondTokens.Length - sequenceLength; index++)
        {
            if (firstSequences.Contains(string.Join(' ', secondTokens, index, sequenceLength))) return true;
        }

        return false;
    }

    private static double Similarity(string first, string second) =>
        Jaccard(
            MeaningfulTokens(first).ToHashSet(StringComparer.Ordinal),
            MeaningfulTokens(second).ToHashSet(StringComparer.Ordinal));

    private static double Jaccard(IReadOnlySet<string> first, IReadOnlySet<string> second)
    {
        if (first.Count == 0 || second.Count == 0) return 0;
        var intersection = first.Count(second.Contains);
        return (double)intersection / (first.Count + second.Count - intersection);
    }

    private static IEnumerable<string> MeaningfulTokens(string value) =>
        Tokenize(value).Where(token => token.Length > 2 && !ComparisonStopWords.Contains(token));

    private static string[] Tokenize(string value) =>
        NormalizeForComparison(value)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static char NormalizeCharacter(char character) => character switch
    {
        'أ' or 'إ' or 'آ' => 'ا',
        'ى' => 'ي',
        'ة' => 'ه',
        _ when char.IsLetterOrDigit(character) => char.ToLowerInvariant(character),
        _ => ' '
    };
}

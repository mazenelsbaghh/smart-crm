using System;
using System.Text.RegularExpressions;

namespace Modules.AI.Services
{
    public static class PricingGuard
    {
        private const RegexOptions MatchOptions =
            RegexOptions.IgnoreCase |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnorePatternWhitespace;

        private static readonly Regex PricingTerms = new(
            """
            (?: سعر | اسعار | أسعار | الاسعار | الأسعار | بكام | تكلفة | تكلفه |
                رسوم | مصاريف | قسط | اقساط | أقساط | تقسيط )
            | \b(?: price | prices | cost | costs | fee | fees | installment | installments )\b
            | \bhow\s+much\b
            """,
            MatchOptions);

        private static readonly Regex ArabicPaymentMethodQuestions = new(
            """
            (?: طرق | طريقة | وسائل | وسيلة | تفاصيل | بيانات )\s+(?:ال)?دفع
            | (?: ازاي | إزاي | كيف | كيفية | فين | أين | اين | وين )\s+(?:ال|أ|ا)?دفع
            | (?:ال|أ|ا)?دفع\s+(?: ازاي | إزاي | كيف | فين | أين | اين | وين )
            | (?: هل(?:\s+(?:أقدر|اقدر))? | ينفع | ممكن | أقدر | اقدر )\s+
              (?:ال|أ|ا)?دفع\s+(?:
                كاش | ب(?:ال)?كاش | نقد(?:ا|ًا)? | فيزا | ب(?:ال)?فيزا |
                بطاقة | البطاقة | ب(?:ال)?بطاقة |
                عن\s+طريق\s+(?: تحويل | فودافون\s+كاش | انستا\s+باي | إنستا\s+باي )
              )
            """,
            MatchOptions);

        private static readonly Regex EnglishPaymentMethodQuestions = new(
            """
            \b(?:
                payment\s+(?: method | methods | option | options | details | instructions | plan | plans )
                | how\s+to\s+pay
                | (?: how | where )\s+(?:(?: can | do | should )\s+)?(?:i\s+)?pay
                | (?: can | could | may )\s+i\s+pay\s+(?: by | with | using )
            )\b
            """,
            MatchOptions);

        public static bool IsPricingQuestion(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            return PricingTerms.IsMatch(content) ||
                   ArabicPaymentMethodQuestions.IsMatch(content) ||
                   EnglishPaymentMethodQuestions.IsMatch(content);
        }

        public static string? BuildPricingReplyFromKnowledge(string knowledgeText)
        {
            if (string.IsNullOrWhiteSpace(knowledgeText))
            {
                return null;
            }

            var monthlyMatch = Regex.Match(
                knowledgeText,
                @"الاشتراك\s+الشهري\s*:\s*([^\n\r.]+)",
                RegexOptions.IgnoreCase);
            var cashMatch = Regex.Match(
                knowledgeText,
                @"عرض\s+الكاش[^\n\r:]*:\s*([^\n\r.]+)",
                RegexOptions.IgnoreCase);

            if (!monthlyMatch.Success && !cashMatch.Success)
            {
                return null;
            }

            var monthly = monthlyMatch.Success ? monthlyMatch.Groups[1].Value.Trim() : null;
            var cash = cashMatch.Success ? cashMatch.Groups[1].Value.Trim() : null;

            if (!string.IsNullOrEmpty(monthly) && !string.IsNullOrEmpty(cash))
            {
                return $"أكيد يا فندم، الأسعار عندنا واضحة:\n\nالاشتراك الشهري: {monthly}.\nالكاش للكورس كامل: {cash}.\n\nتحب أمشي مع حضرتك على نظام الشهري ولا الكاش؟";
            }

            if (!string.IsNullOrEmpty(monthly))
            {
                return $"أكيد يا فندم، الاشتراك الشهري عندنا: {monthly}.\n\nتحب أعرفك المواعيد المتاحة؟";
            }

            return $"أكيد يا فندم، الكاش للكورس كامل: {cash}.\n\nتحب أعرفك المواعيد المتاحة؟";
        }
    }
}

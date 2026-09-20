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

        private static readonly Regex ExactPriceTerms = new(
            """
            (?: سعر | اسعار | أسعار | الاسعار | الأسعار | بكام | تكلفة | تكلفه )
            | \b(?: price | prices | cost | costs )\b
            | \bhow\s+much\b
            """,
            MatchOptions);

        private static readonly Regex ContextualQuestionTerms = new(
            """
            (?: معاد | ميعاد | موعد | مواعيد | امتى | إمتى | متى | مجاني | مجانيه | مجانية |
                سيشن | اونلاين | أونلاين | اوفلاين | أوفلاين | عنوان | مكان | السنتر |
                تفاصيل | معلومات | محتوى | محتوي | نظام | مدة | مده | هتعلم | هستفيد )
            | \b(?: when | where | schedule | session | online | offline | free |
                details? | information | info | curriculum | duration | learn | benefits? | includes? )\b
            | \btell\s+me\s+(?:more|about)\b
            | (?: عرفت | عارف | عارفه | عارفة ).*(?: السعر | التكلفة | سعره )
            | (?: السعر | التكلفة | سعره ).*(?: عرفت | عارف | عارفه | عارفة | خلاص )
            """,
            MatchOptions);

        private static readonly Regex FullCourseTerms = new(
            """
            (?: الكورس | الاشتراك )?\s*(?: كامل | بالكامل | كله )
            | (?: الأربع | الاربع | 4 )\s*(?: شهور | أشهر | اشهر )
            | (?: عرض\s+)?الكاش
            | \b(?: full | whole | total | cash )\b
            """,
            MatchOptions);

        private static readonly Regex MonthlyTerms = new(
            """
            (?: شهري | شهريا | شهرياً | الشهر | الاشتراك )
            | \b(?: monthly | month | subscription )\b
            """,
            MatchOptions);

        private static readonly Regex AdditionalCostQuestions = new(
            """
            (?: مصاريف | رسوم )\s+(?: إضافية | اضافية | إضافيه | اضافيه | تانية | أخرى | اخرى )
            | (?: كتب | الكتاب | الكتب | ماتريال | material | materials ).*(?: مصاريف | رسوم | تكلفة | تكلفه )
            | (?: مصاريف | رسوم | تكلفة | تكلفه ).*(?: كتب | الكتاب | الكتب | ماتريال | material | materials )
            | \b(?: additional | extra )\s+(?: fee | fees | cost | costs )\b
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

        public static bool RequiresExactPriceAnswer(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            return ExactPriceTerms.IsMatch(content) &&
                   !ContextualQuestionTerms.IsMatch(content) &&
                   !AdditionalCostQuestions.IsMatch(content) &&
                   !ArabicPaymentMethodQuestions.IsMatch(content) &&
                   !EnglishPaymentMethodQuestions.IsMatch(content);
        }

        public static string? BuildPricingReplyFromKnowledge(string customerMessage, string knowledgeText)
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

            if (FullCourseTerms.IsMatch(customerMessage) && !string.IsNullOrEmpty(cash))
            {
                return $"سعر الكورس بالكامل كاش هو {cash}.";
            }

            if (MonthlyTerms.IsMatch(customerMessage) && !string.IsNullOrEmpty(monthly))
            {
                return $"الاشتراك الشهري هو {monthly}.";
            }

            if (!string.IsNullOrEmpty(monthly) && !string.IsNullOrEmpty(cash))
            {
                return $"الاشتراك الشهري هو {monthly}، وسعر الكورس بالكامل كاش هو {cash}.";
            }

            if (!string.IsNullOrEmpty(monthly))
            {
                return $"الاشتراك الشهري هو {monthly}.";
            }

            return $"سعر الكورس بالكامل كاش هو {cash}.";
        }
    }
}

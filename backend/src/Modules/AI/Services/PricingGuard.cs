using System;
using System.Text.RegularExpressions;

namespace Modules.AI.Services
{
    public static class PricingGuard
    {
        public static bool IsPricingQuestion(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return false;
            }

            return Regex.IsMatch(
                content,
                "(سعر|اسعار|أسعار|الاسعار|الأسعار|بكام|تكلفة|تكلفه|قسط|اقساط|أقساط|دفع|price|cost|fees)",
                RegexOptions.IgnoreCase);
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

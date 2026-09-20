using Modules.Analytics.Application.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ScheduleDemandLabelNormalizerTests
{
    [Theory]
    [InlineData("الجمعة بعد الساعة 6", "الجمعة مساءً", ScheduleDemandLabelNormalizer.FourToEight)]
    [InlineData("بعد 8 مساء", "موعد مسائي", ScheduleDemandLabelNormalizer.EightToMidnight)]
    [InlineData("مواعيد صباحية", "صباحًا", ScheduleDemandLabelNormalizer.NoonToFour)]
    [InlineData("ينفع الأسبوع اللي جاي", "الأسبوع القادم", ScheduleDemandLabelNormalizer.NextWeek)]
    [InlineData("أول شهر سبتمبر", "شهر سبتمبر", ScheduleDemandLabelNormalizer.NextMonth)]
    [InlineData("ممكن خلال 3 شهور", "بعد ثلاثة شهور", ScheduleDemandLabelNormalizer.NextThreeMonths)]
    [InlineData("يوم الجمعة", "الجمعة والسبت", ScheduleDemandLabelNormalizer.AnyTime)]
    public void Existing_free_text_is_grouped_into_fixed_schedule_categories(
        string requestText,
        string label,
        string expected)
    {
        Assert.Equal(expected, ScheduleDemandLabelNormalizer.Normalize(requestText, label));
    }
}

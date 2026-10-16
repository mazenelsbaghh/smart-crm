using Modules.AI.Services;
using Modules.GroupAppointments.Domain;
using Xunit;

namespace Advertising.UnitTests;

public sealed class FreeSessionBookingPolicyTests
{
    [Fact]
    public void Date_includes_correct_weekday_and_full_Arabic_month()
    {
        Assert.Equal("السبت ١٧ أكتوبر", FreeSessionBookingPolicy.FormatDate(new DateTime(2026, 10, 17)));
    }

    [Theory]
    [InlineData(true, "الخميس ١٥ أكتوبر")]
    [InlineData(false, "الثلاثاء ٢٠ أكتوبر")]
    public void Schedule_uses_free_session_and_assigned_instructor_without_exposing_legacy_course_dates(bool hasFreeSession, string expectedDate)
    {
        var group = new GroupAppointment
        {
            DateTime = new DateTime(2026, 10, 20, 17, 0, 0, DateTimeKind.Utc),
            FreeSessionDateTime = hasFreeSession ? new DateTime(2026, 10, 15, 17, 0, 0, DateTimeKind.Utc) : null,
            CourseSecondDateTime = new DateTime(2026, 10, 22, 17, 0, 0, DateTimeKind.Utc),
            InstructorName = "سارة"
        };
        var details = FreeSessionBookingPolicy.ScheduleDetails(group, TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"));
        Assert.Contains(expectedDate, details);
        Assert.Contains("8:00", details);
        Assert.Contains("سارة", details);
        Assert.DoesNotContain("٢٢ أكتوبر", details);
        if (hasFreeSession) Assert.DoesNotContain("الثلاثاء ٢٠ أكتوبر", details);
    }
}

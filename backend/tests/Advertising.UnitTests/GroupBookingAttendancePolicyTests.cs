using Modules.GroupAppointments.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class GroupBookingAttendancePolicyTests
{
    [Theory]
    [InlineData("الإسكندرية")]
    [InlineData("الاسكندرية")]
    [InlineData("اسكندريه")]
    [InlineData("إسـكندريّة")]
    [InlineData("محافظة الإسكندرية")]
    [InlineData("مدينة اسكندرية، مصر")]
    [InlineData("  aLeXaNdRiA  ")]
    [InlineData("Alex, Egypt")]
    public void Alexandria_residents_can_book_in_person_across_supported_city_spellings(string city)
    {
        var rejection = GroupBookingAttendancePolicy.Rejection(" Offline ", city, "offline");

        Assert.Null(rejection);
    }

    [Theory]
    [InlineData("القاهرة")]
    [InlineData("خارج الإسكندرية")]
    [InlineData("مش من اسكندرية")]
    [InlineData("not Alexandria")]
    [InlineData("Alexandria, USA")]
    public void A_nonlocal_or_negated_city_cannot_authorize_an_in_person_booking(string city)
    {
        var rejection = GroupBookingAttendancePolicy.Rejection("offline", city, "Either");

        Assert.Equal(GroupBookingStatus.OnlineRequired, rejection);
        Assert.Null(GroupBookingAttendancePolicy.Rejection(" Online ", city, "Either"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData(" Missing ")]
    [InlineData("UNKNOWN")]
    [InlineData("n/a")]
    [InlineData("غير محدد")]
    [InlineData("غير معروف")]
    public void Unknown_location_requires_clarification_only_for_an_in_person_booking(string? city)
    {
        Assert.Equal(GroupBookingStatus.CityRequired,
            GroupBookingAttendancePolicy.Rejection("Offline", city, "Unknown"));
        Assert.Null(GroupBookingAttendancePolicy.Rejection("Online", city, "Unknown"));
    }

    [Theory]
    [InlineData("Offline", "Alexandria", " ONLINE ", GroupBookingStatus.OnlineRequired)]
    [InlineData("Offline", null, "Online", GroupBookingStatus.OnlineRequired)]
    [InlineData("Online", "Alexandria", " OFFLINE ", GroupBookingStatus.AttendanceModeMismatch)]
    public void Explicit_attendance_preference_cannot_be_replaced_by_the_selected_group(
        string groupMode, string? city, string preference, GroupBookingStatus expected)
    {
        Assert.Equal(expected, GroupBookingAttendancePolicy.Rejection(groupMode, city, preference));
    }

    [Fact]
    public void Outside_Alexandria_residency_requires_online_even_when_the_model_reports_offline_preference()
    {
        Assert.Null(GroupBookingAttendancePolicy.Rejection("Online", "Cairo", "Offline"));
        Assert.Equal(GroupBookingStatus.OnlineRequired,
            GroupBookingAttendancePolicy.Rejection("Offline", "Cairo", "Offline"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Hybrid")]
    [InlineData("Either")]
    public void A_group_without_a_supported_attendance_mode_cannot_be_booked(string mode)
    {
        Assert.Equal(GroupBookingStatus.InvalidRequest,
            GroupBookingAttendancePolicy.Rejection(mode, "Alexandria", "Either"));
    }

    [Theory]
    [InlineData("Cairo", "Alexandria", GroupBookingStatus.OnlineRequired)]
    [InlineData("Alexandria", "Cairo", null)]
    [InlineData(null, "Cairo", GroupBookingStatus.OnlineRequired)]
    [InlineData("Missing", "Alexandria", null)]
    [InlineData("Unknown", null, GroupBookingStatus.CityRequired)]
    public void Current_city_overrides_saved_city_but_missing_extraction_preserves_saved_evidence(
        string? currentCity, string? savedCity, GroupBookingStatus? expected)
    {
        var effectiveCity = GroupBookingAttendancePolicy.EffectiveCity(currentCity, savedCity);

        Assert.Equal(expected, GroupBookingAttendancePolicy.Rejection("Offline", effectiveCity, "Unknown"));
    }
}

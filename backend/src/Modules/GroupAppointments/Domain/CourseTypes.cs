namespace Modules.GroupAppointments.Domain;

public static class CourseTypes
{
    public const string Regular = "regular";
    public const string Intensive = "intensive";

    public static bool IsValid(string value) => value is Regular or Intensive;
    public static string ArabicLabel(string value) => value == Intensive ? "الكورس المكثف" : "الكورس العادي";
    public static string DefaultGroupName(string courseType, string mode) =>
        $"{ArabicLabel(courseType)} - {(mode == "online" ? "أونلاين" : "في السنتر")}";
}

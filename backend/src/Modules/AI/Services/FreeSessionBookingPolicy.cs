using System.Globalization;
using Modules.GroupAppointments.Domain;

namespace Modules.AI.Services;

internal static class FreeSessionBookingPolicy
{
    public const string Instructions = """
        قاعدة المواعيد الحالية — تتقدم على أي تعليمات أو معرفة قديمة متعارضة:
        المواعيد القابلة للحجز هنا تخص الحصة المجانية فقط. اعرض المواعيد المستقبلية المتاحة المناسبة لنوع الكورس وطريقة الحضور التي يريدها العميل، مع دكتور كل مجموعة كما هو مسجل؛ لا تفترض أن المجانية مع دكتور مصطفى أو أي اسم ثابت.
        عند السؤال عن المواعيد وضّح باختصار أن الحصة المجانية تتيح له معرفة نظام الكورس، ومشاهدة الويب سايت، وتجربة الشرح، ومعرفة مواعيد الكورس المناسبة لمستواه بعد تقييمه في الحصة. لا تحدد له سيشن أولى أو ثانية أو أيام كورس شخصية مسبقًا.
        تقسيم أيام الكورس العام يغطي الأسبوع ما عدا الجمعة إجازة: السبت والأحد، الاثنين والثلاثاء، الأربعاء والخميس. هذا تقسيم عام وليس قائمة حصص مجانية متاحة ولا إثباتًا لتوافر مكان أو ساعة معينة؛ الموعد الشخصي يتحدد حسب المستوى في الحصة المجانية.
        اعرض الحصص المجانية الموجودة فعليًا في القائمة فقط. لا تخترع مواعيد أو أماكن شاغرة، ولا تعرض موعدًا فات أو مجموعة ممتلئة. إن لم توجد مواعيد مناسبة قل إنه لا توجد حصة مجانية متاحة حاليًا للنوع المطلوب.
        عند ذكر أي موعد انسخ اسم اليوم والتاريخ كاملين من البيانات بصيغة مثل «السبت ١٧ أكتوبر» مع الساعة؛ لا تختصره إلى رقم التاريخ وحده ولا تخمّن اليوم.
        قبل الحجز تأكد من اختيار العميل لموعد المجانية المناسب له فقط. ضع suggestedGroupBookingId للمجموعة المختارة واترك تأكيد النجاح النهائي للنظام. لا تكتب رابط التجربة أو عدد الأماكن أو دعوة مرافق من عندك؛ النظام يضيفها بعد نجاح التسجيل. لا تكرر شرح الحصة وتقسيم الأسبوع في كل رد متابعة قصير، وأجب عن سؤال العميل فقط.
        """;

    public static string FormatDate(DateTime localTime)
    {
        var date = localTime.ToString("dddd d MMMM", CultureInfo.GetCultureInfo("ar-EG"));
        return string.Concat(date.Select(c => c is >= '0' and <= '9' ? "٠١٢٣٤٥٦٧٨٩"[c - '0'] : c));
    }

    public static string ScheduleDetails(GroupAppointment group, TimeZoneInfo timezone)
    {
        var session = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(group.FreeSessionDateTime ?? group.DateTime, DateTimeKind.Utc), timezone);
        var date = FormatDate(session) + session.ToString(" الساعة h:mm tt", CultureInfo.GetCultureInfo("ar-EG"));
        var instructor = string.IsNullOrWhiteSpace(group.InstructorName)
            ? "" : $"\n  دكتور المجموعة والحصة المجانية: {group.InstructorName.Trim()}";
        return $"\n  نوع الكورس: {CourseTypes.ArabicLabel(group.CourseType)}\n  ميعاد الحصة المجانية: {date}{instructor}";
    }
}

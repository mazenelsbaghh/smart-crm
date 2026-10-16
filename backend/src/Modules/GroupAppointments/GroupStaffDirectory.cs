using System.Text.Json;
using Modules.Projects.Domain;

namespace Modules.GroupAppointments.Services;

public sealed record GroupStaffContact(string Id, string Name, string Phone);

public sealed record GroupStaffDirectory(GroupStaffContact[] InstructorContacts, GroupStaffContact[] Supervisors)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static GroupStaffDirectory FromSettings(ProjectSettings settings)
    {
        var saved = JsonSerializer.Deserialize<GroupStaffDirectory>(settings.GroupStaffDirectoryJson, JsonOptions);
        var contacts = (saved?.InstructorContacts ?? []).ToList();
        foreach (var name in settings.ActiveInstructors.Split(['\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!contacts.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                contacts.Add(new GroupStaffContact("legacy:" + name, name, ""));
        return new(contacts.ToArray(), saved?.Supervisors ?? []);
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static GroupStaffContact[] Normalize(GroupStaffContact[] contacts, bool phoneRequired)
    {
        if (contacts.Length > 100) throw new ArgumentException("الحد الأقصى ١٠٠ اسم لكل قائمة.");
        var normalized = contacts.Select(contact =>
        {
            if (contact is null || string.IsNullOrWhiteSpace(contact.Id) || contact.Id.Length > 160 ||
                string.IsNullOrWhiteSpace(contact.Name) || contact.Name.Trim().Length > 120 || contact.Phone?.Length > 64)
                throw new ArgumentException("راجع أسماء فريق العمل وأرقامهم.");
            var phone = GroupBookingPhone.Normalize(contact.Phone);
            if (phone is null && (phoneRequired || !string.IsNullOrWhiteSpace(contact.Phone)))
                throw new ArgumentException("اكتب رقم واتساب صحيحًا لكل مسؤولة، ولأي إنستراكتور تريد إضافته للجروب.");
            return contact with { Name = contact.Name.Trim(), Phone = phone ?? "" };
        }).ToArray();
        if (normalized.Select(c => c.Id).Distinct().Count() != normalized.Length ||
            normalized.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length ||
            normalized.Where(c => c.Phone != "").GroupBy(c => c.Phone).Any(g => g.Count() > 1))
            throw new ArgumentException("الأسماء والأرقام لا تتكرر داخل نفس القائمة.");
        return normalized;
    }
}

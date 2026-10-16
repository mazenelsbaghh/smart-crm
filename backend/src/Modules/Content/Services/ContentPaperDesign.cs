using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Modules.Content.Services;

public sealed record ContentPaperStyle(string Id, string Name, string Description);
public sealed record ContentPaperPalette(string Id, string Name, string[] Colors);

public sealed record ContentPaperDesign(Guid ProjectId, string FrameKey, string? TeacherKey,
    string TeacherName, string Style, string[] Colors, string[]? ReferenceKeys = null, string ImageProvider = "Gemini",
    string OpenAiImageModel = OpenAiImageClient.HighestQualityModel)
{
    private const string ProtectionPurpose = "ContentPaperFrame.v1";

    public string Protect(IDataProtectionProvider protection) =>
        protection.CreateProtector(ProtectionPurpose).Protect(JsonSerializer.Serialize(this));

    public static ContentPaperDesign Restore(IDataProtectionProvider protection, Guid projectId, string? token)
    {
        try
        {
            var design = JsonSerializer.Deserialize<ContentPaperDesign>(
                protection.CreateProtector(ProtectionPurpose).Unprotect(token ?? string.Empty));
            if (design is null || design.ProjectId != projectId) throw new ArgumentException("الفريم لا يخص المشروع الحالي.");
            return design;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        { throw new ArgumentException("ولّد الفريم وراجعه أولاً قبل تصميم الورق.", exception); }
    }
}

public static class ContentPaperCatalog
{
    public static readonly ContentPaperStyle[] Styles =
    [
        new("ribbons", "شرائط وملاحظات", "عناوين على شرائط ملونة، قصاصات ورق وملاحظات جانبية، مناسب للشرح والمراجعة."),
        new("stamps", "طوابع وحكايات", "حواف طوابع، قصاصات أطلس ورسومات خافتة، مناسب للتاريخ والجغرافيا والمواد القصصية."),
        new("minimal", "شرح منظم", "فريم خفيف، أقسام واضحة ومساحات هادئة، مناسب للعلوم والمسائل والملخصات.")
    ];
    public static readonly ContentPaperPalette[] Palettes =
    [
        new("mint", "نعناع وشمس", ["#347568", "#F2C34E", "#F8F5E9"]),
        new("atlas", "أزرق وأطلس", ["#285582", "#DCC5A5", "#F8F7F1"]),
        new("plum", "توت ومريمية", ["#70465E", "#9CAD8B", "#FAF5ED"])
    ];
}

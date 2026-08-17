using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Modules.QuranChallenge.Services;

public sealed class QuranVideoGenerator
{
    private const int QuranVerseCount = 6236;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _rendererUrl;

    public QuranVideoGenerator(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _rendererUrl = configuration["QuranChallenge:RendererUrl"] ?? "http://frontend:3000/quran-video/render";
    }

    public async Task<QuranGeneratedVideo> GenerateAsync(
        QuranVerseSelection? selection,
        CancellationToken cancellationToken)
    {
        var verse = selection is null
            ? await RandomEligibleVerseAsync(cancellationToken)
            : await SelectedVerseAsync(selection, cancellationToken);
        var hiddenWordIndex = selection?.HiddenWordIndex ?? Random.Shared.Next(1, verse.Words.Length - 1);
        ValidateHiddenWord(hiddenWordIndex, verse.Words.Length);
        var render = await RenderVideoAsync(verse, hiddenWordIndex, cancellationToken);
        return new QuranGeneratedVideo(verse.SurahNumber, verse.AyahNumber, verse.SurahName,
            verse.Words[hiddenWordIndex], render.VideoBytes, render.DurationSeconds);
    }

    private async Task<QuranVerse> RandomEligibleVerseAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var verse = await QuranVerseAsync(Random.Shared.Next(1, QuranVerseCount + 1).ToString(), cancellationToken);
            if (verse.Words.Length >= 3) return verse;
        }
        throw new InvalidOperationException("تعذّر العثور على آية مناسبة بعد عدة محاولات.");
    }

    private async Task<QuranVerse> SelectedVerseAsync(
        QuranVerseSelection selection,
        CancellationToken cancellationToken)
    {
        if (selection.SurahNumber is < 1 or > 114 || selection.AyahNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(selection), "رقم السورة أو الآية غير صحيح.");
        }
        return await QuranVerseAsync($"{selection.SurahNumber}:{selection.AyahNumber}", cancellationToken);
    }

    private async Task<QuranVerse> QuranVerseAsync(string reference, CancellationToken cancellationToken)
    {
        var url = $"https://api.alquran.cloud/v1/ayah/{reference}/quran-simple";
        var response = await _httpClientFactory.CreateClient().GetFromJsonAsync<QuranApiResponse>(url, cancellationToken);
        var verse = response?.Data ?? throw new InvalidOperationException("لم يصل نص الآية من المصدر.");
        var words = verse.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new QuranVerse(verse.Surah.Number, verse.NumberInSurah, verse.Surah.Name, words);
    }

    private async Task<QuranVideoRender> RenderVideoAsync(
        QuranVerse verse,
        int hiddenWordIndex,
        CancellationToken cancellationToken)
    {
        var request = new { verse.SurahNumber, verse.AyahNumber, hiddenWordIndex };
        using var response = await _httpClientFactory.CreateClient()
            .PostAsJsonAsync(_rendererUrl, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var durationHeader = response.Headers.TryGetValues("X-Quran-Video-Duration", out var values)
            ? values.FirstOrDefault()
            : null;
        if (!double.TryParse(
                durationHeader,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var durationSeconds)
            || durationSeconds <= 0)
        {
            throw new InvalidOperationException("لم يُرجع محرك الفيديو مدة صالحة.");
        }
        return new QuranVideoRender(
            await response.Content.ReadAsByteArrayAsync(cancellationToken),
            durationSeconds);
    }

    private static void ValidateHiddenWord(int hiddenWordIndex, int wordCount)
    {
        if (wordCount < 3 || hiddenWordIndex <= 0 || hiddenWordIndex >= wordCount - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(hiddenWordIndex),
                "الكلمة المختارة يجب أن تكون داخل آية من ثلاث كلمات على الأقل.");
        }
    }

    private sealed record QuranApiResponse([property: JsonPropertyName("data")] QuranApiVerse? Data);
    private sealed record QuranApiVerse(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("numberInSurah")] int NumberInSurah,
        [property: JsonPropertyName("surah")] QuranApiSurah Surah);
    private sealed record QuranApiSurah(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("name")] string Name);
    private sealed record QuranVerse(int SurahNumber, int AyahNumber, string SurahName, string[] Words);
    private sealed record QuranVideoRender(byte[] VideoBytes, double DurationSeconds);
}

public sealed record QuranGeneratedVideo(
    int SurahNumber,
    int AyahNumber,
    string SurahName,
    string HiddenWord,
    byte[] VideoBytes,
    double DurationSeconds)
{
    public string Caption(string template) => template
        .Replace("{surah}", SurahName, StringComparison.Ordinal)
        .Replace("{ayah}", AyahNumber.ToString(), StringComparison.Ordinal)
        .Replace("{word}", HiddenWord, StringComparison.Ordinal);

    public string Title => $"أكمل الآية | {SurahName} | الآية {AyahNumber}";
}

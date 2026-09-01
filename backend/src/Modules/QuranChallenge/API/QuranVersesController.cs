using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Modules.QuranChallenge.API
{
    [ApiController]
    [Route("api/quran/verses")]
    public class QuranVersesController : ControllerBase
    {
        private static readonly HttpClient HttpClient = new();

        [HttpGet("{surahNumber:int}/{ayahNumber:int}")]
        public async Task<IActionResult> GetVerse(int surahNumber, int ayahNumber, CancellationToken cancellationToken)
        {
            if (surahNumber is < 1 or > 114 || ayahNumber < 1)
            {
                return BadRequest(new { error = "اختر رقم سورة من ١ إلى ١١٤ ورقم آية صحيح." });
            }

            using var response = await HttpClient.GetAsync(
                $"https://api.alquran.cloud/v1/ayah/{surahNumber}:{ayahNumber}/quran-simple",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return NotFound(new { error = "لم يتم العثور على الآية المطلوبة." });
            }

            using var payload = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var verse = payload.RootElement.GetProperty("data");
            var text = verse.GetProperty("text").GetString() ?? string.Empty;
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words.Length < 3)
            {
                return BadRequest(new { error = "هذه الآية أقل من ٣ كلمات، اختر آية أخرى." });
            }

            var audioUrl = $"https://everyayah.com/data/Yasser_Ad-Dussary_128kbps/{surahNumber:D3}{ayahNumber:D3}.mp3";
            var surah = verse.GetProperty("surah").GetProperty("name").GetString();

            return Ok(new
            {
                surahNumber,
                ayahNumber,
                surah,
                text,
                words,
                audioUrl,
                source = "Al Quran Cloud / quran-simple",
                selectableWordIndexes = Enumerable.Range(1, words.Length - 2)
            });
        }
    }
}

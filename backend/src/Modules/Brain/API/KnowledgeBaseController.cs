using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Brain.Services;
using Shared.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Modules.AI.Services;

namespace Modules.Brain.API
{
    [ApiController]
    [Route("api")]
    public class KnowledgeBaseController : ControllerBase
    {
        private readonly IKnowledgeBaseService _kbService;
        private readonly AppDbContext _context;
        private readonly IGeminiClient _geminiClient;

        public KnowledgeBaseController(IKnowledgeBaseService kbService, AppDbContext context, IGeminiClient geminiClient)
        {
            _kbService = kbService;
            _context = context;
            _geminiClient = geminiClient;
        }

        [HttpGet("projects/{projectId}/knowledge")]
        public async Task<IActionResult> GetDocuments(Guid projectId)
        {
            var docs = await _context.KnowledgeDocuments
                .Where(d => d.ProjectId == projectId)
                .ToListAsync();
            return Ok(docs);
        }

        [HttpPost("projects/{projectId}/knowledge")]
        public async Task<IActionResult> CreateDocument(Guid projectId, [FromBody] CreateDocumentRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Title) || string.IsNullOrEmpty(request.Content))
            {
                return BadRequest("Title and Content are required");
            }

            var doc = await _kbService.CreateDocumentAsync(projectId, request.Title, request.Content, request.SourceUrl);
            return Created($"/api/knowledge/{doc.Id}", doc);
        }

        [HttpPost("projects/{projectId}/knowledge/suggest")]
        public async Task<IActionResult> SuggestDocument(Guid projectId, [FromBody] CreateDocumentRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Title) || string.IsNullOrEmpty(request.Content))
            {
                return BadRequest("Title and Content are required");
            }

            var doc = await _kbService.SuggestDocumentAsync(projectId, request.Title, request.Content, request.SourceUrl);
            return Created($"/api/knowledge/{doc.Id}", doc);
        }

        [HttpPut("knowledge/{id}/approve")]
        public async Task<IActionResult> ApproveDocument(Guid id)
        {
            try
            {
                var doc = await _kbService.ApproveDocumentAsync(id);
                return Ok(doc);
            }
            catch (ArgumentException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [HttpPut("knowledge/{id}/reject")]
        public async Task<IActionResult> RejectDocument(Guid id)
        {
            try
            {
                var doc = await _kbService.RejectDocumentAsync(id);
                return Ok(doc);
            }
            catch (ArgumentException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [HttpDelete("knowledge/{id}")]
        public async Task<IActionResult> DeleteDocument(Guid id)
        {
            try
            {
                await _kbService.DeleteDocumentAsync(id);
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [HttpPut("knowledge/{id}")]
        public async Task<IActionResult> UpdateDocument(Guid id, [FromBody] UpdateDocumentRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Title) || string.IsNullOrEmpty(request.Content))
            {
                return BadRequest("Title and Content are required");
            }

            try
            {
                var doc = await _kbService.UpdateDocumentAsync(id, request.Title, request.Content, request.SourceUrl);
                return Ok(doc);
            }
            catch (ArgumentException ex)
            {
                return NotFound(ex.Message);
            }
        }

        private string CleanJson(string text)
        {
            var clean = text.Trim();
            if (clean.StartsWith("```"))
            {
                var lines = clean.Split('\n');
                var contentLines = lines.Skip(1).Take(lines.Length - 2);
                clean = string.Join('\n', contentLines).Trim();
            }
            if (clean.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(7).Trim();
            }
            else if (clean.StartsWith("```"))
            {
                clean = clean.Substring(3).Trim();
            }
            if (clean.EndsWith("```"))
            {
                clean = clean.Substring(0, clean.Length - 3).Trim();
            }
            return clean;
        }

        [HttpPost("projects/{projectId}/knowledge/wizard/analyze")]
        public async Task<IActionResult> AnalyzeText(Guid projectId, [FromBody] WizardAnalyzeRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.RawText))
            {
                return BadRequest("Raw text is required");
            }

            var prompt = $@"أنت خبير محترف في تحليل المعلومات وتجهيز قواعد المعرفة للشركات.
مهمتك هي تحليل النص الخام المكتوب باللغة العربية أدناه، واستخراج أي فجوات، أو تفاصيل غير واضحة، أو افتراضات ناقصة يحتاجها بوت خدمة العملاء ليكون دقيقاً وشاملاً.
قم بتوليد قائمة من الأسئلة التوضيحية باللغة العربية.
لكل سؤال، اقترح بالضبط 3 خيارات إجابة تغطي الاحتمالات الأكثر شيوعاً، ليتمكن المستخدم من الاختيار بينها بسهولة أو كتابة إجابة مخصصة.
يجب أن يكون الناتج بصيغة JSON صالحة كالتالي دون أي نص تمهيدي أو علامات تخفيض (markdown tags) ودون أي رمز ```json:
[
  {{
    ""question"": ""ما هي أوقات العمل الرسمية لديكم؟"",
    ""options"": [
      ""من الساعة 9 صباحاً حتى 5 مساءً من الأحد للخميس"",
      ""على مدار 24 ساعة طوال أيام الأسبوع"",
      ""من السبت للخميس من 10 صباحاً حتى 10 مساءً""
    ]
  }}
]

النص المراد تحليله:
{request.RawText}";

            try
            {
                var aiResponse = await _geminiClient.GenerateReplyAsync(prompt);
                var cleanedJson = CleanJson(aiResponse);
                var questions = System.Text.Json.JsonSerializer.Deserialize<List<WizardQuestionDto>>(cleanedJson, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                return Ok(questions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in AnalyzeText: {ex.Message}");
                return StatusCode(500, "حدث خطأ أثناء تحليل النص بالذكاء الاصطناعي.");
            }
        }

        [HttpPost("projects/{projectId}/knowledge/wizard/generate")]
        public async Task<IActionResult> GenerateQas(Guid projectId, [FromBody] WizardGenerateRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.RawText))
            {
                return BadRequest("Raw text is required");
            }

            var answersBuilder = new System.Text.StringBuilder();
            if (request.Answers != null)
            {
                foreach (var ans in request.Answers)
                {
                    answersBuilder.AppendLine($"- السؤال: {ans.Question}");
                    answersBuilder.AppendLine($"  الإجابة: {ans.Answer}");
                }
            }

            var prompt = $@"أنت خبير محترف في صياغة الأسئلة والأجوبة لقواعد المعرفة.
قم بدمج النص الخام الأصلي مع إجابات المستخدم على الأسئلة التوضيحية المرفقة أدناه.
قم بصياغة مجموعة شاملة ودقيقة من الأسئلة والأجوبة (Q&A Pairs) باللغة العربية التي تغطي كل التفاصيل بشكل كامل.
يجب أن يكون الناتج بصيغة JSON صالحة كالتالي دون أي نص تمهيدي أو علامات تخفيض (markdown tags) ودون أي رمز ```json:
[
  {{
    ""question"": ""سؤال محدد باللغة العربية؟"",
    ""answer"": ""إجابة تفصيلية وواضحة جداً بناءً على المعلومات المقدمة.""
  }}
]

النص الخام الأصلي:
{request.RawText}

إجابات المستخدم التوضيحية:
{answersBuilder}";

            try
            {
                var aiResponse = await _geminiClient.GenerateReplyAsync(prompt);
                var cleanedJson = CleanJson(aiResponse);
                var qaPairs = System.Text.Json.JsonSerializer.Deserialize<List<WizardQaPairDto>>(cleanedJson, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                return Ok(qaPairs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GenerateQas: {ex.Message}");
                return StatusCode(500, "حدث خطأ أثناء توليد الأسئلة والأجوبة بالذكاء الاصطناعي.");
            }
        }
    }

    public class WizardAnalyzeRequest
    {
        public string RawText { get; set; } = string.Empty;
    }

    public class WizardQuestionDto
    {
        public string Question { get; set; } = string.Empty;
        public List<string> Options { get; set; } = new();
    }

    public class WizardGenerateRequest
    {
        public string RawText { get; set; } = string.Empty;
        public List<WizardAnswerDto> Answers { get; set; } = new();
    }

    public class WizardAnswerDto
    {
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
    }

    public class WizardQaPairDto
    {
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
    }

    public class CreateDocumentRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? SourceUrl { get; set; }
    }

    public class UpdateDocumentRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? SourceUrl { get; set; }
    }
}

using Microsoft.AspNetCore.Mvc;
using Modules.Brain.Services;
using System;
using System.Threading.Tasks;

namespace Modules.Brain.API
{
    [ApiController]
    [Route("api")]
    public class KnowledgeBaseController : ControllerBase
    {
        private readonly IKnowledgeBaseService _kbService;

        public KnowledgeBaseController(IKnowledgeBaseService kbService)
        {
            _kbService = kbService;
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
    }

    public class CreateDocumentRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? SourceUrl { get; set; }
    }
}

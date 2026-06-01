using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Brain.Services;
using Shared.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Modules.Brain.API
{
    [ApiController]
    [Route("api")]
    public class KnowledgeBaseController : ControllerBase
    {
        private readonly IKnowledgeBaseService _kbService;
        private readonly AppDbContext _context;

        public KnowledgeBaseController(IKnowledgeBaseService kbService, AppDbContext context)
        {
            _kbService = kbService;
            _context = context;
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

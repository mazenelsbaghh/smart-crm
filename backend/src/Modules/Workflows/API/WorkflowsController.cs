using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Workflows.Domain;
using Shared.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Modules.Workflows.API
{
    [ApiController]
    [Route("api")]
    public class WorkflowsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public WorkflowsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost("projects/{projectId}/workflows")]
        public async Task<IActionResult> CreateWorkflow(Guid projectId, [FromBody] CreateWorkflowRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Name) || string.IsNullOrEmpty(request.TriggerType))
            {
                return BadRequest("Name and TriggerType are required.");
            }

            var workflow = new AutomationWorkflow
            {
                ProjectId = projectId,
                Name = request.Name,
                TriggerType = request.TriggerType,
                FiltersJson = request.FiltersJson ?? "{}",
                ActionsJson = request.ActionsJson ?? "[]",
                IsActive = request.IsActive,
                Version = 1
            };

            _context.AutomationWorkflows.Add(workflow);
            await _context.SaveChangesAsync();

            return Created($"/api/workflows/{workflow.Id}", workflow);
        }

        [HttpGet("projects/{projectId}/workflows")]
        public async Task<IActionResult> GetWorkflows(Guid projectId)
        {
            var workflows = await _context.AutomationWorkflows
                .Where(w => w.ProjectId == projectId)
                .ToListAsync();
            return Ok(workflows);
        }

        [HttpGet("workflows/{id}")]
        public async Task<IActionResult> GetWorkflow(Guid id)
        {
            var workflow = await _context.AutomationWorkflows.FindAsync(id);
            if (workflow == null) return NotFound();
            return Ok(workflow);
        }

        [HttpPut("workflows/{id}")]
        public async Task<IActionResult> UpdateWorkflow(Guid id, [FromBody] UpdateWorkflowRequest request)
        {
            var workflow = await _context.AutomationWorkflows.FindAsync(id);
            if (workflow == null) return NotFound();

            workflow.Name = request.Name ?? workflow.Name;
            workflow.TriggerType = request.TriggerType ?? workflow.TriggerType;
            workflow.FiltersJson = request.FiltersJson ?? workflow.FiltersJson;
            workflow.ActionsJson = request.ActionsJson ?? workflow.ActionsJson;
            if (request.IsActive.HasValue)
            {
                workflow.IsActive = request.IsActive.Value;
            }

            await _context.SaveChangesAsync();
            return Ok(workflow);
        }

        [HttpDelete("workflows/{id}")]
        public async Task<IActionResult> DeleteWorkflow(Guid id)
        {
            var workflow = await _context.AutomationWorkflows.FindAsync(id);
            if (workflow == null) return NotFound();

            _context.AutomationWorkflows.Remove(workflow);
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }

    public class CreateWorkflowRequest
    {
        public string Name { get; set; } = string.Empty;
        public string TriggerType { get; set; } = string.Empty;
        public string FiltersJson { get; set; } = "{}";
        public string ActionsJson { get; set; } = "[]";
        public bool IsActive { get; set; } = true;
    }

    public class UpdateWorkflowRequest
    {
        public string? Name { get; set; }
        public string? TriggerType { get; set; }
        public string? FiltersJson { get; set; }
        public string? ActionsJson { get; set; }
        public bool? IsActive { get; set; }
    }
}

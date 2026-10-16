using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Approvals.Domain;
using Modules.Approvals.Services;
using Shared.Infrastructure;
using Shared.Security;
using System.Security.Claims;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Modules.Approvals.API
{
    [ApiController]
    [Route("api")]
    public class ApprovalsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IRiskAnalyzer _riskAnalyzer;

        public ApprovalsController(AppDbContext context, IRiskAnalyzer riskAnalyzer)
        {
            _context = context;
            _riskAnalyzer = riskAnalyzer;
        }

        [HttpGet("projects/{projectId}/approvals")]
        public async Task<IActionResult> GetApprovals(Guid projectId, [FromQuery] string? status = null)
        {
            var query = _context.ApprovalRequests.Where(a => a.ProjectId == projectId);
            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(a => a.Status == status);
            }
            var approvals = await query.ToListAsync();
            return Ok(approvals);
        }

        [HttpPost("projects/{projectId}/actions/execute")]
        public async Task<IActionResult> ExecuteAction(Guid projectId, [FromBody] ExecuteActionRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.ActionType) || string.IsNullOrEmpty(request.PayloadJson))
            {
                return BadRequest("ActionType and PayloadJson are required.");
            }
            if (!IsSupportedAction(request.ActionType)) return UnsupportedAction();
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var requesterId)) return Unauthorized();

            var riskLevel = _riskAnalyzer.Analyze(request.ActionType, request.PayloadJson);
            if (riskLevel == "High" || riskLevel == "Critical")
            {
                var approval = new ApprovalRequest
                {
                    ProjectId = projectId,
                    ActionType = request.ActionType,
                    PayloadJson = request.PayloadJson,
                    RiskLevel = riskLevel,
                    Status = "Pending",
                    RequestedBy = requesterId.ToString()
                };

                _context.ApprovalRequests.Add(approval);
                await _context.SaveChangesAsync();

                return Accepted(new { id = approval.Id, status = "Pending", riskLevel });
            }

            // Low or Medium risk executes immediately
            try
            {
                await ExecuteActionInternalAsync(request.ActionType, request.PayloadJson);
                await _context.SaveChangesAsync();
                return Ok(new { status = "Executed", riskLevel });
            }
            catch (Exception ex) when (IsInvalidActionPayload(ex))
            {
                return BadRequest($"Action execution failed: {ex.Message}");
            }
        }

        [HttpPost("approvals/{id}/approve")]
        [ProjectPermission("approve_actions")]
        public async Task<IActionResult> ApproveRequest(Guid id)
        {
            if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var approverId)) return Unauthorized();
            var approval = await _context.ApprovalRequests.FindAsync(id);
            if (approval == null) return NotFound();

            if (approval.Status != "Pending")
            {
                return BadRequest($"Approval request is already in '{approval.Status}' status.");
            }

            if (!IsSupportedAction(approval.ActionType)) return UnsupportedAction();

            try
            {
                await ExecuteActionInternalAsync(approval.ActionType, approval.PayloadJson);
                approval.Status = "Approved";
                approval.ExecutedAt = DateTime.UtcNow;
                approval.ApprovedBy = approverId.ToString();
                await _context.SaveChangesAsync();
                return Ok(new { id = approval.Id, status = "Approved", executedAt = approval.ExecutedAt });
            }
            catch (Exception ex) when (IsInvalidActionPayload(ex))
            {
                return BadRequest($"Action execution failed: {ex.Message}");
            }
        }

        [HttpPost("approvals/{id}/reject")]
        [ProjectPermission("approve_actions")]
        public async Task<IActionResult> RejectRequest(Guid id, [FromBody] RejectRequestPayload? payload = null)
        {
            var approval = await _context.ApprovalRequests.FindAsync(id);
            if (approval == null) return NotFound();

            if (approval.Status != "Pending")
            {
                return BadRequest($"Approval request is already in '{approval.Status}' status.");
            }

            approval.Status = "Rejected";
            if (payload != null && !string.IsNullOrEmpty(payload.Notes))
            {
                approval.Notes = payload.Notes;
            }

            await _context.SaveChangesAsync();
            return Ok(new { id = approval.Id, status = "Rejected" });
        }

        private async Task ExecuteActionInternalAsync(string actionType, string payloadJson)
        {
            if (string.Equals(actionType, "CRMUpdate", StringComparison.OrdinalIgnoreCase))
            {
                using var doc = JsonDocument.Parse(payloadJson);
                var root = doc.RootElement;

                if (!root.TryGetProperty("customerId", out var customerIdProp) && !root.TryGetProperty("CustomerId", out customerIdProp))
                {
                    throw new ArgumentException("customerId is required in payloadJson.");
                }

                var customerId = Guid.Parse(customerIdProp.GetString()!);
                var customer = await _context.Customers.FindAsync(customerId);
                if (customer == null)
                {
                    throw new KeyNotFoundException($"Customer with ID {customerId} not found.");
                }

                if (root.TryGetProperty("budget", out var budgetProp) || root.TryGetProperty("Budget", out budgetProp))
                {
                    var budgetVal = budgetProp.GetDecimal();
                    customer.Budget = budgetVal;

                    var activeDeal = await _context.Deals
                        .FirstOrDefaultAsync(d => d.CustomerId == customerId && d.Status == Modules.CRM.Domain.DealStatus.Open);
                    if (activeDeal != null)
                    {
                        activeDeal.Amount = budgetVal;
                        _context.Entry(activeDeal).State = EntityState.Modified;
                    }
                }

                if (root.TryGetProperty("city", out var cityProp) || root.TryGetProperty("City", out cityProp))
                {
                    customer.City = cityProp.GetString();
                }

                if (root.TryGetProperty("leadScore", out var lsProp) || root.TryGetProperty("LeadScore", out lsProp))
                {
                    customer.LeadScore = lsProp.GetInt32();
                }

                if (root.TryGetProperty("notes", out var notesProp) || root.TryGetProperty("Notes", out notesProp))
                {
                    customer.Notes = notesProp.GetString() ?? string.Empty;
                }
            }
            else
            {
                throw new NotSupportedException("This action has no executor.");
            }
        }

        private static bool IsSupportedAction(string actionType) =>
            string.Equals(actionType, "CRMUpdate", StringComparison.OrdinalIgnoreCase);

        private IActionResult UnsupportedAction() => UnprocessableEntity(new
        {
            code = "ACTION_NOT_SUPPORTED",
            message = "This action has no executor and cannot be approved or executed."
        });

        private static bool IsInvalidActionPayload(Exception exception) => exception is
            JsonException or ArgumentException or FormatException or InvalidOperationException or KeyNotFoundException or OverflowException;
    }

    public class ExecuteActionRequest
    {
        public string ActionType { get; set; } = string.Empty;
        public string PayloadJson { get; set; } = "{}";
        public string? RequestedBy { get; set; }
    }

    public class RejectRequestPayload
    {
        public string? Notes { get; set; }
    }
}

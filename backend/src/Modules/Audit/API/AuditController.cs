using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Shared.Infrastructure;
using Shared.Security;

namespace Modules.Audit.API
{
    [ApiController]
    [Route("api/projects/{projectId}/audit")]
    [Authorize]
    public class AuditController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ITenantContext _tenantContext;

        public AuditController(AppDbContext context, ITenantContext tenantContext)
        {
            _context = context;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> QueryAuditLogs(
            Guid projectId,
            [FromQuery] string? action,
            [FromQuery] Guid? user,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to)
        {
            if (projectId != _tenantContext.ProjectId)
            {
                return StatusCode(403, new { error = "Access to this project's audit logs is not allowed." });
            }

            // Set AppDbContext tenant context so global query filters apply
            // To ensure strict multi-tenant isolation, we also filter explicitly by projectId
            var query = _context.AuditLogs
                .Where(a => a.ProjectId == projectId);

            if (!string.IsNullOrEmpty(action))
            {
                query = query.Where(a => a.Action.Equals(action, StringComparison.OrdinalIgnoreCase));
            }

            if (user.HasValue && user.Value != Guid.Empty)
            {
                query = query.Where(a => a.UserId == user.Value);
            }

            if (from.HasValue)
            {
                query = query.Where(a => a.Timestamp >= from.Value.ToUniversalTime());
            }

            if (to.HasValue)
            {
                query = query.Where(a => a.Timestamp <= to.Value.ToUniversalTime());
            }

            var logs = await query
                .OrderByDescending(a => a.Timestamp)
                .ToListAsync();

            return Ok(new
            {
                totalCount = logs.Count,
                logs = logs
            });
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Events;
using Modules.Search.Application.Services;
using Modules.Search.Workers;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Modules.Search.API
{
    [ApiController]
    [Route("api")]
    public class SearchController : ControllerBase
    {
        private readonly ISearchService _searchService;
        private readonly AppDbContext _context;
        private readonly IEventBus _eventBus;

        public SearchController(ISearchService searchService, AppDbContext context, IEventBus eventBus)
        {
            _searchService = searchService;
            _context = context;
            _eventBus = eventBus;
        }

        [HttpGet("projects/{projectId}/search")]
        public async Task<IActionResult> Search(Guid projectId, [FromQuery] string q, [FromQuery] string? type = null)
        {
            if (string.IsNullOrEmpty(q))
            {
                return BadRequest("Search query parameter 'q' is required.");
            }

            var results = await _searchService.SearchAsync(projectId, q, type);
            return Ok(new
            {
                totalMatches = results.Count(),
                matches = results.Select(r => new
                {
                    id = r.EntityId,
                    type = r.EntityType,
                    title = r.Title,
                    snippet = r.Content,
                    timestamp = r.CreatedAt
                })
            });
        }

        // Trigger manual full reindexing of all project data into Elasticsearch
        [HttpPost("projects/{projectId}/search/reindex")]
        public async Task<IActionResult> Reindex(Guid projectId)
        {
            try
            {
                // Fetch Customers
                var customers = await _context.Customers
                    .IgnoreQueryFilters()
                    .Where(c => c.ProjectId == projectId)
                    .ToListAsync();
                foreach (var customer in customers)
                {
                    await _eventBus.PublishAsync(new EntityIndexedEvent
                    {
                        EntityId = customer.Id,
                        EntityType = "Customer",
                        ProjectId = projectId,
                        Action = "Upsert",
                        ContentJson = JsonSerializer.Serialize(customer)
                    });
                }

                // Fetch Messages by joining with Conversations to get ProjectId
                var messages = await _context.Messages
                    .IgnoreQueryFilters()
                    .Join(_context.Conversations.IgnoreQueryFilters(),
                        m => m.ConversationId,
                        c => c.Id,
                        (m, c) => new { Message = m, Conversation = c })
                    .Where(x => x.Conversation.ProjectId == projectId)
                    .Select(x => x.Message)
                    .ToListAsync();
                foreach (var msg in messages)
                {
                    await _eventBus.PublishAsync(new EntityIndexedEvent
                    {
                        EntityId = msg.Id,
                        EntityType = "Message",
                        ProjectId = projectId,
                        Action = "Upsert",
                        ContentJson = JsonSerializer.Serialize(msg)
                    });
                }

                // Fetch Conversations
                var conversations = await _context.Conversations
                    .IgnoreQueryFilters()
                    .Where(c => c.ProjectId == projectId)
                    .ToListAsync();
                foreach (var convo in conversations)
                {
                    await _eventBus.PublishAsync(new EntityIndexedEvent
                    {
                        EntityId = convo.Id,
                        EntityType = "Conversation",
                        ProjectId = projectId,
                        Action = "Upsert",
                        ContentJson = JsonSerializer.Serialize(convo)
                    });
                }

                return Ok(new { message = $"Reindexing triggered for {customers.Count} customers, {messages.Count} messages, and {conversations.Count} conversations." });
            }
            catch (Exception ex)
            {
                return BadRequest($"Reindexing failed: {ex.Message}");
            }
        }
    }
}

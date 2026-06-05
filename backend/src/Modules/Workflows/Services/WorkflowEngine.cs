using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Modules.Workflows.Domain;
using Shared.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Modules.Workflows.Services
{
    public interface IWorkflowEngine
    {
        Task ProcessEventAsync(Guid projectId, string triggerType, Guid customerId, object eventData);
    }

    public class WorkflowEngine : IWorkflowEngine
    {
        private readonly AppDbContext _dbContext;
        private readonly Shared.Queue.IEventBus _eventBus;
        private readonly Microsoft.AspNetCore.SignalR.IHubContext<Modules.Conversations.Hubs.NotificationHub> _hubContext;

        public WorkflowEngine(
            AppDbContext dbContext,
            Shared.Queue.IEventBus eventBus,
            Microsoft.AspNetCore.SignalR.IHubContext<Modules.Conversations.Hubs.NotificationHub> hubContext)
        {
            _dbContext = dbContext;
            _eventBus = eventBus;
            _hubContext = hubContext;
        }

        public async Task ProcessEventAsync(Guid projectId, string triggerType, Guid customerId, object eventData)
        {
            // Fetch active workflows for this project and trigger
            var workflows = await _dbContext.AutomationWorkflows
                .Where(w => w.ProjectId == projectId && w.TriggerType == triggerType && w.IsActive)
                .ToListAsync();

            foreach (var workflow in workflows)
            {
                bool matches = false;
                try
                {
                    matches = EvaluateFilter(workflow.FiltersJson, eventData);
                }
                catch (Exception ex)
                {
                    await LogExecutionAsync(projectId, workflow.Id, customerId, triggerType, false, $"Filter evaluation failed: {ex.Message}");
                    continue;
                }

                if (matches)
                {
                    try
                    {
                        var actionsExecuted = await ExecuteActionsAsync(projectId, workflow.ActionsJson, customerId);
                        await LogExecutionAsync(projectId, workflow.Id, customerId, triggerType, true, string.Empty, actionsExecuted);
                    }
                    catch (Exception ex)
                    {
                        await LogExecutionAsync(projectId, workflow.Id, customerId, triggerType, false, $"Action execution failed: {ex.Message}");
                    }
                }
            }
        }

        private bool EvaluateFilter(string filtersJson, object eventData)
        {
            if (string.IsNullOrWhiteSpace(filtersJson) || filtersJson == "{}" || filtersJson == "[]")
            {
                return true;
            }

            var filters = JsonSerializer.Deserialize<Dictionary<string, object>>(filtersJson);
            if (filters == null || !filters.Any())
            {
                return true;
            }

            // For CustomerTagAdded: check if the event contains the expected tag
            if (eventData is Shared.Events.CustomerTagAddedEvent tagEvent)
            {
                if (filters.TryGetValue("tag", out var expectedTagObj))
                {
                    var expectedTag = expectedTagObj?.ToString();
                    return string.Equals(expectedTag, tagEvent.Tag, StringComparison.OrdinalIgnoreCase);
                }
            }

            // Default fallback if filter is not known or eventData type is not matched
            return false;
        }

        private async Task<string> ExecuteActionsAsync(Guid projectId, string actionsJson, Guid customerId)
        {
            if (string.IsNullOrWhiteSpace(actionsJson) || actionsJson == "[]")
            {
                return "[]";
            }

            var actions = JsonSerializer.Deserialize<List<WorkflowAction>>(actionsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (actions == null || !actions.Any())
            {
                return "[]";
            }

            var customer = await _dbContext.Customers.FindAsync(customerId);
            if (customer == null)
            {
                throw new Exception($"Customer with ID {customerId} not found.");
            }

            foreach (var action in actions)
            {
                if (string.Equals(action.Type, "UpdateCRM", StringComparison.OrdinalIgnoreCase))
                {
                    if (action.Parameters != null)
                    {
                        if (action.Parameters.TryGetValue("leadScore", out var leadScoreObj))
                        {
                            if (leadScoreObj is JsonElement jsonEl && jsonEl.ValueKind == JsonValueKind.Number)
                            {
                                customer.LeadScore = jsonEl.GetInt32();
                            }
                            else if (leadScoreObj is int lsInt)
                            {
                                customer.LeadScore = lsInt;
                            }
                        }

                        if (action.Parameters.TryGetValue("notes", out var notesObj))
                        {
                            var newNotes = notesObj?.ToString() ?? string.Empty;
                            if (!string.IsNullOrEmpty(newNotes))
                            {
                                customer.Notes = string.IsNullOrEmpty(customer.Notes) 
                                    ? newNotes 
                                    : $"{customer.Notes}\n{newNotes}";
                            }
                        }

                        if (action.Parameters.TryGetValue("city", out var cityObj))
                        {
                            customer.City = cityObj?.ToString() ?? customer.City;
                        }

                        if (action.Parameters.TryGetValue("name", out var nameObj))
                        {
                            customer.Name = nameObj?.ToString() ?? customer.Name;
                        }
                    }
                }
                else if (string.Equals(action.Type, "SendMessage", StringComparison.OrdinalIgnoreCase))
                {
                    if (action.Parameters != null && action.Parameters.TryGetValue("text", out var textObj))
                    {
                        var text = textObj?.ToString() ?? string.Empty;
                        // Replace placeholders
                        text = text.Replace("{{CustomerName}}", customer.Name ?? "عميلنا العزيز")
                                   .Replace("{{PhoneNumber}}", customer.PhoneNumber);

                        await _eventBus.PublishAsync(new Shared.Events.AIReplyGeneratedEvent
                        {
                            ProjectId = projectId,
                            Sender = customer.PhoneNumber,
                            Content = text,
                            Buttons = Array.Empty<string>()
                        });
                    }
                }
                else if (string.Equals(action.Type, "SendAlert", StringComparison.OrdinalIgnoreCase))
                {
                    if (action.Parameters != null)
                    {
                        var title = action.Parameters.TryGetValue("title", out var titleObj) ? titleObj?.ToString() ?? "تنبيه تلقائي" : "تنبيه تلقائي";
                        var message = action.Parameters.TryGetValue("message", out var msgObj) ? msgObj?.ToString() ?? string.Empty : string.Empty;
                        var severity = action.Parameters.TryGetValue("severity", out var sevObj) ? sevObj?.ToString() ?? "Info" : "Info";

                        // Replace placeholders
                        message = message.Replace("{{CustomerName}}", customer.Name ?? "عميلنا العزيز")
                                         .Replace("{{PhoneNumber}}", customer.PhoneNumber);

                        var alert = new Modules.Conversations.Domain.NotificationAlert
                        {
                            ProjectId = projectId,
                            UserId = Guid.Empty, // System alert
                            Type = severity,
                            Message = $"{title}: {message}",
                            IsRead = false
                        };

                        _dbContext.NotificationAlerts.Add(alert);
                        await _dbContext.SaveChangesAsync(); // generate Id and timestamps

                        await _hubContext.Clients.Group($"project_{projectId}").SendAsync("ReceiveNotification", new
                        {
                            id = alert.Id,
                            type = alert.Type,
                            message = alert.Message,
                            createdAt = alert.CreatedAt.ToString("o"),
                            payload = new
                            {
                                customerId = customer.Id,
                                severity = severity
                            }
                        });
                    }
                }
            }

            await _dbContext.SaveChangesAsync();
            return JsonSerializer.Serialize(actions);
        }

        private async Task LogExecutionAsync(Guid projectId, Guid workflowId, Guid customerId, string triggerType, bool success, string errorMessage, string actionsExecutedJson = "[]")
        {
            var log = new WorkflowExecutionLog
            {
                ProjectId = projectId,
                AutomationWorkflowId = workflowId,
                CustomerId = customerId,
                TriggerType = triggerType,
                Success = success,
                ErrorMessage = errorMessage,
                ActionsExecutedJson = actionsExecutedJson,
                ExecutedAt = DateTime.UtcNow
            };

            _dbContext.WorkflowExecutionLogs.Add(log);
            await _dbContext.SaveChangesAsync();
        }
    }

    public class WorkflowAction
    {
        public string Type { get; set; } = string.Empty;
        public Dictionary<string, object>? Parameters { get; set; }
    }
}

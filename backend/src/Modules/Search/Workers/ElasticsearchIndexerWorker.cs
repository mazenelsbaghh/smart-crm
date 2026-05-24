using System;
using System.Text.Json;
using System.Threading.Tasks;
using Elastic.Clients.Elasticsearch;
using Shared.Events;
using Shared.Queue;

namespace Modules.Search.Workers
{
    public class SearchDocument
    {
        public string Id { get; set; } // e.g. "Customer_uuid" or "Message_uuid"
        public string EntityId { get; set; }
        public string EntityType { get; set; }
        public string ProjectId { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ElasticsearchIndexerWorker : IIntegrationEventHandler<EntityIndexedEvent>
    {
        private readonly ElasticsearchClient _elasticClient;
        private const string IndexName = "smart_whatsapp_search";

        public ElasticsearchIndexerWorker(ElasticsearchClient elasticClient)
        {
            _elasticClient = elasticClient;
        }

        public async Task HandleAsync(EntityIndexedEvent @event)
        {
            Console.WriteLine($"[ElasticsearchIndexerWorker] Received EntityIndexedEvent for {@event.EntityType} ID: {@event.EntityId}");

            var docId = $"{@event.EntityType}_{@event.EntityId}";

            if (string.Equals(@event.Action, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await _elasticClient.DeleteAsync<SearchDocument>(docId, d => d.Index(IndexName));
                    Console.WriteLine($"[ElasticsearchIndexerWorker] Deleted document {docId} from Elasticsearch.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ElasticsearchIndexerWorker] Error deleting from Elasticsearch: {ex.Message}");
                }
                return;
            }

            // Upsert document
            try
            {
                // Create index if not exists
                var existsResponse = await _elasticClient.Indices.ExistsAsync(IndexName);
                if (!existsResponse.Exists)
                {
                    await _elasticClient.Indices.CreateAsync(IndexName);
                }

                // Extract text fields depending on EntityType
                string title = "";
                string content = "";
                DateTime createdAt = DateTime.UtcNow;

                using var doc = JsonDocument.Parse(@event.ContentJson);
                var root = doc.RootElement;

                if (string.Equals(@event.EntityType, "Message", StringComparison.OrdinalIgnoreCase))
                {
                    content = root.TryGetProperty("Content", out var textProp) || root.TryGetProperty("content", out textProp) ? textProp.GetString() ?? "" : "";
                    title = root.TryGetProperty("Sender", out var sendProp) || root.TryGetProperty("sender", out sendProp) ? sendProp.GetString() ?? "" : "WhatsApp Message";
                }
                else if (string.Equals(@event.EntityType, "Customer", StringComparison.OrdinalIgnoreCase))
                {
                    title = (root.TryGetProperty("Name", out var nameProp) || root.TryGetProperty("name", out nameProp)) ? nameProp.GetString() ?? "" : "";
                    content = $"{(root.TryGetProperty("City", out var cityProp) || root.TryGetProperty("city", out cityProp) ? cityProp.GetString() ?? "" : "")} {(root.TryGetProperty("Notes", out var noteProp) || root.TryGetProperty("notes", out noteProp) ? noteProp.GetString() ?? "" : "")}";
                }
                else if (string.Equals(@event.EntityType, "Conversation", StringComparison.OrdinalIgnoreCase))
                {
                    title = $"Conversation with {(root.TryGetProperty("CustomerName", out var cnameProp) || root.TryGetProperty("customerName", out cnameProp) ? cnameProp.GetString() ?? "" : "")}";
                    content = (root.TryGetProperty("Status", out var statusProp) || root.TryGetProperty("status", out statusProp)) ? statusProp.GetString() ?? "" : "";
                }

                if (root.TryGetProperty("CreatedAt", out var catProp) || root.TryGetProperty("createdAt", out catProp))
                {
                    if (catProp.ValueKind == JsonValueKind.String)
                    {
                        DateTime.TryParse(catProp.GetString(), out createdAt);
                    }
                }

                var searchDoc = new SearchDocument
                {
                    Id = docId,
                    EntityId = @event.EntityId.ToString(),
                    EntityType = @event.EntityType,
                    ProjectId = @event.ProjectId.ToString(),
                    Title = title,
                    Content = content,
                    CreatedAt = createdAt
                };

                await _elasticClient.IndexAsync(searchDoc, idx => idx.Index(IndexName));
                Console.WriteLine($"[ElasticsearchIndexerWorker] Indexed document {docId} successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ElasticsearchIndexerWorker] Error indexing in Elasticsearch: {ex.Message}");
            }
        }
    }
}

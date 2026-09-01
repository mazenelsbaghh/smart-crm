using System;
using System.Text.Json;

namespace Modules.WhatsApp.Services
{
    /// <summary>
    /// Converts structured AI output into the text that customers should receive and see in the CRM.
    /// </summary>
    public static class OutgoingMessageText
    {
        public static string Normalize(string content)
        {
            if (string.IsNullOrWhiteSpace(content) || !content.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                return content;
            }

            try
            {
                using var document = JsonDocument.Parse(content);
                foreach (var propertyName in new[] { "whatsapp_message", "message", "text" })
                {
                    if (document.RootElement.TryGetProperty(propertyName, out var property)
                        && property.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(property.GetString()))
                    {
                        return property.GetString()!;
                    }
                }
            }
            catch (JsonException)
            {
                // Keep non-conforming content intact instead of dropping a customer message.
            }

            return content;
        }
    }
}

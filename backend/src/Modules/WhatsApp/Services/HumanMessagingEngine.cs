using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Modules.WhatsApp.Services
{
    public interface IHumanMessagingEngine
    {
        IEnumerable<string> SplitIntoChunks(string content);
        int CalculateTypingDelay(string chunk);
    }

    public class HumanMessagingEngine : IHumanMessagingEngine
    {
        public IEnumerable<string> SplitIntoChunks(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                yield break;

            // Split by double newlines first (logical paragraphs)
            var paragraphs = content.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var para in paragraphs)
            {
                var trimmed = para.Trim();
                if (trimmed.Length > 150)
                {
                    // Split into smaller sentences if a paragraph is too long
                    var sentences = Regex.Split(trimmed, @"(?<=[.!?])\s+");
                    foreach (var sentence in sentences)
                    {
                        var sTrimmed = sentence.Trim();
                        if (!string.IsNullOrEmpty(sTrimmed))
                        {
                            yield return sTrimmed;
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(trimmed))
                {
                    yield return trimmed;
                }
            }
        }

        public int CalculateTypingDelay(string chunk)
        {
            if (string.IsNullOrEmpty(chunk)) return 0;
            // 50ms per character, min 1s, max 4s
            int delay = chunk.Length * 50;
            return Math.Clamp(delay, 1000, 4000);
        }
    }
}

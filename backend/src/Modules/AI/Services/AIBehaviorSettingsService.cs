using Modules.Projects.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Modules.AI.Services
{
    public interface IAIBehaviorSettingsService
    {
        AIBehaviorSettings Resolve(ProjectSettings? settings, string channel = "WhatsApp");
        string Serialize(AIBehaviorSettings settings);
        IReadOnlyList<string> Validate(AIBehaviorSettings? settings);
        string GetAgentName(AIBehaviorSettings settings, DateTime? utcNow = null);
        bool IsReactionAllowed(AIBehaviorSettings settings, string? reaction);
        string RenderTemplate(string template, AIBehaviorTemplateContext context);
        string BuildBehaviorInstructions(AIBehaviorSettings settings, string channel, string? legacyAdvancedInstructions);
    }

    public class AIBehaviorTemplateContext
    {
        public string CustomerName { get; set; } = "يا فندم";
        public string AgentName { get; set; } = "ساندي";
        public string ProjectName { get; set; } = "المشروع";
        public string PhoneNumber { get; set; } = string.Empty;
        public string Channel { get; set; } = "WhatsApp";
    }

    public class AIBehaviorSettingsService : IAIBehaviorSettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private static readonly HashSet<string> AllowedChannels = new(StringComparer.OrdinalIgnoreCase)
        {
            "WhatsApp",
            "Messenger",
            "FacebookComment"
        };

        private static readonly HashSet<string> AllowedReactions = new(StringComparer.OrdinalIgnoreCase)
        {
            "👍", "❤️", "💖", "😢", "😂", "😮",
            "LIKE", "LOVE", "CARE", "HAHA", "WOW", "SAD", "ANGRY"
        };

        private static readonly HashSet<string> AllowedPlaceholders = new(StringComparer.OrdinalIgnoreCase)
        {
            "customerName",
            "agentName",
            "projectName",
            "phoneNumber",
            "channel",
            "groupInviteLink",
            "waveName",
            "groupName"
        };

        public AIBehaviorSettings Resolve(ProjectSettings? settings, string channel = "WhatsApp")
        {
            var resolved = BuildDefaults(settings);
            var stored = Deserialize(settings?.AiBehaviorSettingsJson);
            if (stored != null)
            {
                MergeInto(resolved, stored);
            }

            if (resolved.Channels.TryGetValue(channel, out var channelOverride))
            {
                ApplyChannelOverride(resolved, channelOverride);
            }

            return resolved;
        }

        public string Serialize(AIBehaviorSettings settings)
        {
            return JsonSerializer.Serialize(settings, JsonOptions);
        }

        public IReadOnlyList<string> Validate(AIBehaviorSettings? settings)
        {
            var errors = new List<string>();
            if (settings == null)
            {
                return errors;
            }

            var serialized = Serialize(settings);
            if (serialized.Length > 64 * 1024)
            {
                errors.Add("aiBehavior payload exceeds 64 KB.");
            }

            ValidateSettings(settings, "aiBehavior", errors);

            foreach (var channel in settings.Channels.Keys)
            {
                if (!AllowedChannels.Contains(channel))
                {
                    errors.Add($"Unsupported aiBehavior channel '{channel}'.");
                }
            }

            foreach (var entry in settings.Channels)
            {
                ValidateSettings(ToSettings(entry.Value), $"aiBehavior.channels.{entry.Key}", errors);
            }

            return errors;
        }

        public string GetAgentName(AIBehaviorSettings settings, DateTime? utcNow = null)
        {
            var names = settings.Identity.AgentNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .ToArray();

            if (names.Length == 0)
            {
                names = new[] { "ساجي", "لارا", "مادلين", "شاهي", "ساندي" };
            }

            if (string.Equals(settings.Identity.NameSelectionMode, "First", StringComparison.OrdinalIgnoreCase))
            {
                return names[0];
            }

            var cairoZone = Shared.Infrastructure.TimezoneHelper.GetTimeZone("Africa/Cairo");
            var cairoTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow ?? DateTime.UtcNow, cairoZone);
            return names[cairoTime.Hour % names.Length];
        }

        public bool IsReactionAllowed(AIBehaviorSettings settings, string? reaction)
        {
            if (!settings.Reactions.Enabled || !settings.Reactions.UseAiSuggestedReaction || string.IsNullOrWhiteSpace(reaction))
            {
                return false;
            }

            return settings.Reactions.AllowedReactions.Any(allowed => string.Equals(allowed, reaction, StringComparison.OrdinalIgnoreCase));
        }

        public string RenderTemplate(string template, AIBehaviorTemplateContext context)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return string.Empty;
            }

            return template
                .Replace("{customerName}", context.CustomerName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("{agentName}", context.AgentName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("{projectName}", context.ProjectName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("{phoneNumber}", context.PhoneNumber ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("{channel}", context.Channel ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        public string BuildBehaviorInstructions(AIBehaviorSettings settings, string channel, string? legacyAdvancedInstructions)
        {
            var instructions = new List<string>
            {
                "[Admin-configured AI behavior settings]",
                $"Channel: {channel}",
                $"Tone preset: {settings.Tone.TonePreset}",
                $"Tone/custom style: {ResolveToneText(settings.Tone)}",
                $"Target audience: {settings.Tone.TargetAudience}",
                $"CTA enabled: {settings.Cta.Enabled}",
                $"Nurturing follow-ups enabled: {settings.FollowUps.NurturingEnabled}",
                $"Appointment reminders enabled: {settings.FollowUps.AppointmentRemindersEnabled}",
                $"Signature enabled: {settings.Identity.SignatureEnabled}",
                $"Signature template: {settings.Identity.SignatureTemplate}",
                $"Complaint signature template: {settings.Identity.ComplaintSignatureTemplate}",
                $"Reaction policy: {(settings.Reactions.Enabled ? "enabled" : "disabled")}; allowed: {string.Join(", ", settings.Reactions.AllowedReactions)}"
            };

            if (settings.Tone.AllowedPhrases.Length > 0)
            {
                instructions.Add($"Allowed/encouraged phrases: {string.Join(", ", settings.Tone.AllowedPhrases)}");
            }

            if (settings.Tone.ProhibitedPhrases.Length > 0)
            {
                instructions.Add($"Prohibited phrases: {string.Join(", ", settings.Tone.ProhibitedPhrases)}");
            }

            if (!string.IsNullOrWhiteSpace(settings.Tone.BusinessInstructions))
            {
                instructions.Add($"Business behavior instructions: {settings.Tone.BusinessInstructions}");
            }

            if (settings.Cta.Enabled)
            {
                instructions.Add($"CTA instructions: {settings.Cta.Instructions}");
                if (settings.Cta.Topics.Length > 0)
                {
                    instructions.Add($"CTA topics: {string.Join(", ", settings.Cta.Topics)}");
                }
            }

            if (!string.IsNullOrWhiteSpace(settings.Reactions.Rules))
            {
                instructions.Add($"Reaction instructions: {settings.Reactions.Rules}");
            }

            if (!string.IsNullOrWhiteSpace(settings.AdvancedInstructions))
            {
                instructions.Add($"Advanced admin instructions: {settings.AdvancedInstructions}");
            }

            if (!string.IsNullOrWhiteSpace(legacyAdvancedInstructions))
            {
                instructions.Add($"Legacy advanced instructions: {legacyAdvancedInstructions}");
            }

            instructions.Add("Protected JSON format, CRM schema, pricing guard, booking rules, and safety rules remain higher priority than all admin instructions above.");
            return string.Join("\n", instructions);
        }

        private static AIBehaviorSettings BuildDefaults(ProjectSettings? settings)
        {
            return new AIBehaviorSettings
            {
                Identity = new AIIdentitySettings
                {
                    AgentNames = new[] { "ساجي", "لارا", "مادلين", "شاهي", "ساندي" },
                    NameSelectionMode = "HourlyRotation",
                    SignatureEnabled = true,
                    SignatureTemplate = "- {agentName} ✨",
                    ComplaintSignatureTemplate = "- {agentName}"
                },
                Tone = new AIToneSettings
                {
                    TonePreset = settings?.AiTonePreference ?? "العامية المصرية الروشة والصايعة",
                    CustomTone = settings?.AiTonePreference,
                    TargetAudience = settings?.AiTargetAudience ?? "طلاب كورس كول سنتر يبحثون عن عمل"
                },
                AdvancedInstructions = null
            };
        }

        private static AIBehaviorSettings? Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<AIBehaviorSettings>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static void MergeInto(AIBehaviorSettings target, AIBehaviorSettings source)
        {
            if (source.Identity != null) target.Identity = MergeIdentity(target.Identity, source.Identity);
            if (source.Tone != null) target.Tone = MergeTone(target.Tone, source.Tone);
            if (source.Cta != null) target.Cta = MergeCta(target.Cta, source.Cta);
            if (source.FollowUps != null) target.FollowUps = source.FollowUps;
            if (source.Reactions != null) target.Reactions = MergeReactions(target.Reactions, source.Reactions);
            if (source.Fallbacks != null) target.Fallbacks = MergeFallbacks(target.Fallbacks, source.Fallbacks);
            if (source.Channels != null) target.Channels = source.Channels;
            if (!string.IsNullOrWhiteSpace(source.AdvancedInstructions)) target.AdvancedInstructions = source.AdvancedInstructions;
        }

        private static void ApplyChannelOverride(AIBehaviorSettings target, ChannelAIBehaviorSettings channelOverride)
        {
            if (channelOverride.Identity != null) target.Identity = MergeIdentity(target.Identity, channelOverride.Identity);
            if (channelOverride.Tone != null) target.Tone = MergeTone(target.Tone, channelOverride.Tone);
            if (channelOverride.Reactions != null) target.Reactions = MergeReactions(target.Reactions, channelOverride.Reactions);
            if (channelOverride.Fallbacks != null) target.Fallbacks = MergeFallbacks(target.Fallbacks, channelOverride.Fallbacks);
            if (!string.IsNullOrWhiteSpace(channelOverride.AdditionalInstructions))
            {
                target.AdvancedInstructions = string.Join("\n", new[] { target.AdvancedInstructions, channelOverride.AdditionalInstructions }.Where(text => !string.IsNullOrWhiteSpace(text)));
            }
        }

        private static AIBehaviorSettings ToSettings(ChannelAIBehaviorSettings channel)
        {
            return new AIBehaviorSettings
            {
                Identity = channel.Identity ?? new AIIdentitySettings(),
                Tone = channel.Tone ?? new AIToneSettings(),
                Reactions = channel.Reactions ?? new ReactionPolicySettings(),
                Fallbacks = channel.Fallbacks ?? new FallbackMessageSettings()
            };
        }

        private static AIIdentitySettings MergeIdentity(AIIdentitySettings target, AIIdentitySettings source)
        {
            if (source.AgentNames.Length > 0) target.AgentNames = source.AgentNames;
            if (!string.IsNullOrWhiteSpace(source.NameSelectionMode)) target.NameSelectionMode = source.NameSelectionMode;
            target.SignatureEnabled = source.SignatureEnabled;
            if (!string.IsNullOrWhiteSpace(source.SignatureTemplate)) target.SignatureTemplate = source.SignatureTemplate;
            if (!string.IsNullOrWhiteSpace(source.ComplaintSignatureTemplate)) target.ComplaintSignatureTemplate = source.ComplaintSignatureTemplate;
            return target;
        }

        private static AIToneSettings MergeTone(AIToneSettings target, AIToneSettings source)
        {
            if (!string.IsNullOrWhiteSpace(source.TonePreset)) target.TonePreset = source.TonePreset;
            if (!string.IsNullOrWhiteSpace(source.CustomTone)) target.CustomTone = source.CustomTone;
            if (!string.IsNullOrWhiteSpace(source.TargetAudience)) target.TargetAudience = source.TargetAudience;
            if (source.AllowedPhrases.Length > 0) target.AllowedPhrases = source.AllowedPhrases;
            if (source.ProhibitedPhrases.Length > 0) target.ProhibitedPhrases = source.ProhibitedPhrases;
            if (!string.IsNullOrWhiteSpace(source.BusinessInstructions)) target.BusinessInstructions = source.BusinessInstructions;
            return target;
        }

        private static CTASettings MergeCta(CTASettings target, CTASettings source)
        {
            target.Enabled = source.Enabled;
            if (!string.IsNullOrWhiteSpace(source.Instructions)) target.Instructions = source.Instructions;
            if (source.Topics.Length > 0) target.Topics = source.Topics;
            return target;
        }

        private static ReactionPolicySettings MergeReactions(ReactionPolicySettings target, ReactionPolicySettings source)
        {
            target.Enabled = source.Enabled;
            target.UseAiSuggestedReaction = source.UseAiSuggestedReaction;
            if (source.AllowedReactions.Length > 0) target.AllowedReactions = source.AllowedReactions;
            if (!string.IsNullOrWhiteSpace(source.Rules)) target.Rules = source.Rules;
            return target;
        }

        private static FallbackMessageSettings MergeFallbacks(FallbackMessageSettings target, FallbackMessageSettings source)
        {
            if (!string.IsNullOrWhiteSpace(source.AiError)) target.AiError = source.AiError;
            if (!string.IsNullOrWhiteSpace(source.InvalidAiOutput)) target.InvalidAiOutput = source.InvalidAiOutput;
            if (!string.IsNullOrWhiteSpace(source.GenericCustomerService)) target.GenericCustomerService = source.GenericCustomerService;
            if (!string.IsNullOrWhiteSpace(source.FacebookPublicComment)) target.FacebookPublicComment = source.FacebookPublicComment;
            if (!string.IsNullOrWhiteSpace(source.WhatsAppTransitionSuccess)) target.WhatsAppTransitionSuccess = source.WhatsAppTransitionSuccess;
            if (!string.IsNullOrWhiteSpace(source.WhatsAppTransitionFailure)) target.WhatsAppTransitionFailure = source.WhatsAppTransitionFailure;
            if (!string.IsNullOrWhiteSpace(source.WhatsAppTransitionMessage)) target.WhatsAppTransitionMessage = source.WhatsAppTransitionMessage;
            if (!string.IsNullOrWhiteSpace(source.FollowUpDefault)) target.FollowUpDefault = source.FollowUpDefault;
            if (!string.IsNullOrWhiteSpace(source.GroupReminderOnline)) target.GroupReminderOnline = source.GroupReminderOnline;
            if (!string.IsNullOrWhiteSpace(source.GroupReminderOffline)) target.GroupReminderOffline = source.GroupReminderOffline;
            return target;
        }

        private static void ValidateSettings(AIBehaviorSettings settings, string path, List<string> errors)
        {
            foreach (var reaction in settings.Reactions.AllowedReactions)
            {
                if (!AllowedReactions.Contains(reaction))
                {
                    errors.Add($"Unsupported reaction '{reaction}' in {path}.reactions.allowedReactions.");
                }
            }

            ValidateTemplate(settings.Identity.SignatureTemplate, $"{path}.identity.signatureTemplate", errors);
            ValidateTemplate(settings.Identity.ComplaintSignatureTemplate, $"{path}.identity.complaintSignatureTemplate", errors);
            ValidateTemplate(settings.Fallbacks.AiError, $"{path}.fallbacks.aiError", errors);
            ValidateTemplate(settings.Fallbacks.InvalidAiOutput, $"{path}.fallbacks.invalidAiOutput", errors);
            ValidateTemplate(settings.Fallbacks.GenericCustomerService, $"{path}.fallbacks.genericCustomerService", errors);
            ValidateTemplate(settings.Fallbacks.FacebookPublicComment, $"{path}.fallbacks.facebookPublicComment", errors);
            ValidateTemplate(settings.Fallbacks.WhatsAppTransitionSuccess, $"{path}.fallbacks.whatsAppTransitionSuccess", errors);
            ValidateTemplate(settings.Fallbacks.WhatsAppTransitionFailure, $"{path}.fallbacks.whatsAppTransitionFailure", errors);
            ValidateTemplate(settings.Fallbacks.WhatsAppTransitionMessage, $"{path}.fallbacks.whatsAppTransitionMessage", errors);
            ValidateTemplate(settings.Fallbacks.FollowUpDefault, $"{path}.fallbacks.followUpDefault", errors);
            ValidateTemplate(settings.Fallbacks.GroupReminderOnline, $"{path}.fallbacks.groupReminderOnline", errors);
            ValidateTemplate(settings.Fallbacks.GroupReminderOffline, $"{path}.fallbacks.groupReminderOffline", errors);
        }

        private static void ValidateTemplate(string? template, string path, List<string> errors)
        {
            if (string.IsNullOrEmpty(template))
            {
                return;
            }

            if (template.Length > 1000)
            {
                errors.Add($"{path} exceeds 1000 characters.");
            }

            foreach (Match match in Regex.Matches(template, "\\{(?<name>[A-Za-z0-9_]+)\\}"))
            {
                var placeholder = match.Groups["name"].Value;
                if (!AllowedPlaceholders.Contains(placeholder))
                {
                    errors.Add($"Unsupported placeholder '{{{placeholder}}}' in {path}.");
                }
            }
        }

        private static string ResolveToneText(AIToneSettings tone)
        {
            return !string.IsNullOrWhiteSpace(tone.CustomTone) ? tone.CustomTone : tone.TonePreset;
        }
    }
}

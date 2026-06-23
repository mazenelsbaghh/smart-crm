using System;

namespace Shared.Events
{
    public class CRMUpdateSuggestedEvent : IntegrationEvent
    {
        public Guid ProjectId { get; set; }
        public Guid CustomerId { get; set; }
        public string Sender { get; set; } = string.Empty;
        public string City { get; set; }
        public decimal? Budget { get; set; }
        public string[] Interests { get; set; } = Array.Empty<string>();
        public string Timeline { get; set; }
        public string Intent { get; set; }
        public string Sentiment { get; set; }
        public double Confidence { get; set; }
        public string Label { get; set; }
        public string PipelineStage { get; set; } = "New";
        public bool FollowUpNeeded { get; set; }
        public string? FollowUpType { get; set; }
        public string? FollowUpAppointmentTime { get; set; }
        public string? FollowUpDueDate { get; set; }
        public string? FollowUpNotes { get; set; }
        public string[]? AIInsights { get; set; }
    }
}

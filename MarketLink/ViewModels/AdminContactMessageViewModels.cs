namespace MarketLink.ViewModels
{
    public class AdminContactMessagesIndexViewModel
    {
        public List<AdminContactMessageRowViewModel> Messages { get; set; } = new();

        public string Search { get; set; } = "";
        public string Status { get; set; } = "";
        public string InquiryType { get; set; } = "";

        public int TotalCount { get; set; }
        public int NewCount { get; set; }
        public int InProgressCount { get; set; }
        public int RepliedCount { get; set; }
        public int ResolvedCount { get; set; }
    }

    public class AdminContactMessageRowViewModel
    {
        public int AuditLogId { get; set; }
        public string Reference { get; set; } = "";
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string? Phone { get; set; }
        public string InquiryType { get; set; } = "General";
        public string Subject { get; set; } = "";
        public string MessagePreview { get; set; } = "";
        public string Status { get; set; } = "New";
        public DateTime CreatedAt { get; set; }
        public int? TargetFarmerId { get; set; }
        public string? TargetFarmerName { get; set; }
    }

    public class AdminContactMessageDetailsViewModel
    {
        public int AuditLogId { get; set; }
        public string Reference { get; set; } = "";
        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public string? Phone { get; set; }
        public string InquiryType { get; set; } = "General";
        public string Subject { get; set; } = "";
        public string Message { get; set; } = "";
        public string Status { get; set; } = "New";
        public DateTime CreatedAt { get; set; }

        public int? TargetFarmerId { get; set; }
        public string? TargetFarmerName { get; set; }

        public string? AdminNote { get; set; }
        public DateTime? StatusUpdatedAt { get; set; }
        public int? StatusUpdatedByUserId { get; set; }

        public int? SubmittedByUserId { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }

        public string ReplyMailtoUrl { get; set; } = "";
    }

    public class AdminContactMessageUpdateViewModel
    {
        public int AuditLogId { get; set; }
        public string Status { get; set; } = "New";
        public string? AdminNote { get; set; }
    }
}

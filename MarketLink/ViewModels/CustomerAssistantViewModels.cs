using System.ComponentModel.DataAnnotations;

namespace MarketLink.ViewModels
{
    public class CustomerAssistantPageViewModel
    {
        public int? ConversationId { get; set; }

        [StringLength(500)]
        public string Question { get; set; } = "";

        public List<CustomerAssistantMessageViewModel> Messages { get; set; } = new();

        public List<CustomerAssistantConversationViewModel> Conversations { get; set; } = new();

        public List<string> ExampleQuestions { get; set; } = new()
        {
            "Where can I get tomatoes this Saturday?",
            "Which farmers have spinach?",
            "What time does Green Market open?",
            "Can I pick up around 11?",
            "Show products from my favorite farmer."
        };
    }

    public class CustomerAssistantMessageViewModel
    {
        public int AssistantMessageId { get; set; }
        public string Role { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public List<CustomerAssistantActionViewModel> Actions { get; set; } = new();
    }

    public class CustomerAssistantConversationViewModel
    {
        public int AssistantConversationId { get; set; }
        public string Title { get; set; } = "";
        public DateTime LastActivityAt { get; set; }
    }

    public class CustomerAssistantActionViewModel
    {
        public string Label { get; set; } = "";
        public string Controller { get; set; } = "";
        public string Action { get; set; } = "Index";
        public int? Id { get; set; }
    }

    public class MarketLinkAssistantReply
    {
        public string Text { get; set; } = "";
        public string Intent { get; set; } = "";
        public Dictionary<string, object?> Metadata { get; set; } = new();
        public List<CustomerAssistantActionViewModel> Actions { get; set; } = new();
    }
}

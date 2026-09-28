namespace MarketLink.Services
{
    public sealed record EmailSendResult(
        bool Sent,
        string? Error = null);

    public interface IEmailService
    {
        Task<EmailSendResult> SendNotificationAsync(
            string recipientEmail,
            string recipientName,
            string subject,
            string message,
            string? actionUrl,
            CancellationToken cancellationToken = default);
    }
}

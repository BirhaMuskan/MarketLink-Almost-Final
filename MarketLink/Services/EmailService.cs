using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;

namespace MarketLink.Services
{
    public sealed class EmailService : IEmailService
    {
        private readonly EmailSettings _settings;
        private readonly ILogger<EmailService> _logger;

        public EmailService(
            IOptions<EmailSettings> options,
            ILogger<EmailService> logger)
        {
            _settings = options.Value;
            _logger = logger;
        }

        public async Task<EmailSendResult> SendNotificationAsync(
            string recipientEmail,
            string recipientName,
            string subject,
            string message,
            string? actionUrl,
            CancellationToken cancellationToken = default)
        {
            if (!_settings.Enabled)
            {
                return new EmailSendResult(
                    false,
                    "Email delivery is disabled in EmailSettings.");
            }

            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                return new EmailSendResult(
                    false,
                    "Recipient email address is missing.");
            }

            if (string.IsNullOrWhiteSpace(_settings.Host) ||
                string.IsNullOrWhiteSpace(_settings.SenderEmail))
            {
                return new EmailSendResult(
                    false,
                    "SMTP host or sender email is not configured.");
            }

            try
            {
                var absoluteActionUrl = BuildAbsoluteActionUrl(actionUrl);
                var html = BuildHtmlBody(
                    recipientName,
                    subject,
                    message,
                    absoluteActionUrl);

                using var mail = new MailMessage
                {
                    From = new MailAddress(
                        _settings.SenderEmail,
                        string.IsNullOrWhiteSpace(_settings.SenderName)
                            ? "MarketLink"
                            : _settings.SenderName),
                    Subject = subject,
                    Body = html,
                    IsBodyHtml = true
                };

                mail.To.Add(new MailAddress(recipientEmail));

                using var smtp = new SmtpClient(
                    _settings.Host,
                    _settings.Port)
                {
                    EnableSsl = _settings.EnableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false
                };

                if (!string.IsNullOrWhiteSpace(_settings.Username))
                {
                    smtp.Credentials = new NetworkCredential(
                        _settings.Username,
                        _settings.Password);
                }

                cancellationToken.ThrowIfCancellationRequested();
                await smtp.SendMailAsync(mail, cancellationToken);

                return new EmailSendResult(true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "MarketLink email notification failed for {RecipientEmail}.",
                    recipientEmail);

                return new EmailSendResult(
                    false,
                    ex.Message.Length > 950
                        ? ex.Message[..950]
                        : ex.Message);
            }
        }

        private string? BuildAbsoluteActionUrl(string? actionUrl)
        {
            if (string.IsNullOrWhiteSpace(actionUrl))
                return null;

            if (Uri.TryCreate(actionUrl, UriKind.Absolute, out var absolute))
                return absolute.ToString();

            if (string.IsNullOrWhiteSpace(_settings.AppBaseUrl))
                return null;

            if (!Uri.TryCreate(
                    _settings.AppBaseUrl.TrimEnd('/') + "/",
                    UriKind.Absolute,
                    out var baseUri))
            {
                return null;
            }

            return Uri.TryCreate(
                    baseUri,
                    actionUrl.TrimStart('/'),
                    out var combined)
                ? combined.ToString()
                : null;
        }

        private static string BuildHtmlBody(
            string recipientName,
            string subject,
            string message,
            string? actionUrl)
        {
            var encoder = HtmlEncoder.Default;
            var safeName = encoder.Encode(
                string.IsNullOrWhiteSpace(recipientName)
                    ? "there"
                    : recipientName);
            var safeSubject = encoder.Encode(subject);
            var safeMessage = encoder.Encode(message)
                .Replace("\r\n", "<br />")
                .Replace("\n", "<br />");

            var button = string.IsNullOrWhiteSpace(actionUrl)
                ? ""
                : $@"<div style='margin-top:24px;'>
                        <a href='{encoder.Encode(actionUrl)}'
                           style='display:inline-block;background:#234f1e;color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:8px;font-weight:600;'>
                            View in MarketLink
                        </a>
                    </div>";

            return $@"<!doctype html>
<html>
<body style='margin:0;padding:0;background:#f5f7f5;font-family:Arial,Helvetica,sans-serif;color:#1f2937;'>
    <div style='max-width:620px;margin:0 auto;padding:28px 16px;'>
        <div style='background:#ffffff;border:1px solid #e5e7eb;border-radius:14px;overflow:hidden;'>
            <div style='padding:22px 26px;background:#173b2a;color:#ffffff;'>
                <div style='font-size:24px;font-weight:700;'>MarketLink</div>
                <div style='font-size:13px;opacity:.86;margin-top:4px;'>Fresh Markets · Local Farmers</div>
            </div>
            <div style='padding:28px 26px;'>
                <p style='margin:0 0 18px;'>Hi {safeName},</p>
                <h2 style='margin:0 0 14px;font-size:21px;color:#173b2a;'>{safeSubject}</h2>
                <div style='font-size:15px;line-height:1.65;'>{safeMessage}</div>
                {button}
                <p style='margin:28px 0 0;font-size:12px;color:#6b7280;'>
                    This is an automated notification from MarketLink.
                </p>
            </div>
        </div>
    </div>
</body>
</html>";
        }
    }
}

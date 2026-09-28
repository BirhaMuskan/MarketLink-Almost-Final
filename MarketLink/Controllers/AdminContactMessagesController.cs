using System.Security.Claims;
using System.Text.Json;
using MarketLink.Models;
using MarketLink.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminContactMessagesController : Controller
    {
        private readonly ApplicationDbContext _context;

        private static readonly HashSet<string> AllowedStatuses =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "New",
                "In Progress",
                "Replied",
                "Resolved"
            };

        public AdminContactMessagesController(
            ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string? search = null,
            string? status = null,
            string? inquiryType = null,
            CancellationToken cancellationToken = default)
        {
            var logs = await _context.auditlogs
                .AsNoTracking()
                .Where(x =>
                    x.Action == "WebsiteContact" &&
                    x.EntityName == "ContactMessage")
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync(cancellationToken);

            var rows = logs
                .Select(ParseRow)
                .Where(x => x != null)
                .Cast<AdminContactMessageRowViewModel>()
                .ToList();

            var allRows = rows.ToList();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var needle = search.Trim();

                rows = rows
                    .Where(x =>
                        Contains(x.Reference, needle) ||
                        Contains(x.Name, needle) ||
                        Contains(x.Email, needle) ||
                        Contains(x.Subject, needle) ||
                        Contains(x.InquiryType, needle) ||
                        Contains(x.TargetFarmerName, needle))
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                rows = rows
                    .Where(x =>
                        string.Equals(
                            x.Status,
                            status,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (!string.IsNullOrWhiteSpace(inquiryType))
            {
                rows = rows
                    .Where(x =>
                        string.Equals(
                            x.InquiryType,
                            inquiryType,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var model =
                new AdminContactMessagesIndexViewModel
                {
                    Messages = rows,
                    Search = search ?? "",
                    Status = status ?? "",
                    InquiryType = inquiryType ?? "",

                    TotalCount = allRows.Count,
                    NewCount = allRows.Count(x => x.Status == "New"),
                    InProgressCount =
                        allRows.Count(x => x.Status == "In Progress"),
                    RepliedCount =
                        allRows.Count(x => x.Status == "Replied"),
                    ResolvedCount =
                        allRows.Count(x => x.Status == "Resolved")
                };

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Details(
            int id,
            CancellationToken cancellationToken = default)
        {
            var log = await _context.auditlogs
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    x =>
                        x.AuditLogId == id &&
                        x.Action == "WebsiteContact" &&
                        x.EntityName == "ContactMessage",
                    cancellationToken);

            if (log == null)
                return NotFound();

            var model = ParseDetails(log);

            if (model == null)
                return NotFound();

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Update(
            AdminContactMessageUpdateViewModel model,
            CancellationToken cancellationToken)
        {
            var log = await _context.auditlogs
                .FirstOrDefaultAsync(
                    x =>
                        x.AuditLogId == model.AuditLogId &&
                        x.Action == "WebsiteContact" &&
                        x.EntityName == "ContactMessage",
                    cancellationToken);

            if (log == null)
                return NotFound();

            if (!AllowedStatuses.Contains(model.Status))
            {
                TempData["ContactAdminError"] =
                    "Please select a valid message status.";

                return RedirectToAction(
                    nameof(Details),
                    new { id = model.AuditLogId });
            }

            int? adminUserId = null;

            if (int.TryParse(
                    User.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var parsedAdminId))
            {
                adminUserId = parsedAdminId;
            }

            var workflow =
                new ContactWorkflowState
                {
                    Status = NormalizeStatus(model.Status),
                    AdminNote = string.IsNullOrWhiteSpace(model.AdminNote)
                        ? null
                        : model.AdminNote.Trim(),
                    UpdatedAt = DateTime.Now,
                    UpdatedByUserId = adminUserId
                };

            // OldValuesJson is unused by the contact submission itself,
            // so it stores the admin workflow state without requiring
            // a schema change/migration.
            log.OldValuesJson =
                JsonSerializer.Serialize(workflow);

            await _context.SaveChangesAsync(
                cancellationToken);

            TempData["ContactAdminSuccess"] =
                $"Message marked as {workflow.Status}.";

            return RedirectToAction(
                nameof(Details),
                new { id = model.AuditLogId });
        }

        private static AdminContactMessageRowViewModel? ParseRow(
            AuditLog log)
        {
            var payload = ParsePayload(log.NewValuesJson);

            if (payload == null)
                return null;

            var workflow =
                ParseWorkflow(log.OldValuesJson);

            return new AdminContactMessageRowViewModel
            {
                AuditLogId = log.AuditLogId,
                Reference =
                    FirstNonBlank(
                        payload.Reference,
                        log.EntityId,
                        $"Contact-{log.AuditLogId}"),
                Name = payload.Name ?? "Unknown",
                Email = payload.Email ?? "",
                Phone = payload.Phone,
                InquiryType =
                    FirstNonBlank(
                        payload.InquiryType,
                        "General"),
                Subject =
                    FirstNonBlank(
                        payload.Subject,
                        "Website inquiry"),
                MessagePreview =
                    BuildPreview(payload.Message),
                Status =
                    NormalizeStatus(
                        workflow?.Status ?? "New"),
                CreatedAt = log.CreatedAt,
                TargetFarmerId =
                    payload.TargetFarmerId,
                TargetFarmerName =
                    payload.TargetFarmerName
            };
        }

        private static AdminContactMessageDetailsViewModel? ParseDetails(
            AuditLog log)
        {
            var payload = ParsePayload(log.NewValuesJson);

            if (payload == null)
                return null;

            var workflow =
                ParseWorkflow(log.OldValuesJson);

            var reference =
                FirstNonBlank(
                    payload.Reference,
                    log.EntityId,
                    $"Contact-{log.AuditLogId}");

            var email =
                payload.Email ?? "";

            var subject =
                FirstNonBlank(
                    payload.Subject,
                    "MarketLink inquiry");

            var replyBody =
                $"Hello {payload.Name ?? "there"},\n\n" +
                $"Thank you for contacting MarketLink regarding \"{subject}\".\n\n" +
                $"Reference: {reference}\n\n";

            var mailto =
                string.IsNullOrWhiteSpace(email)
                    ? ""
                    : "mailto:" +
                      Uri.EscapeDataString(email) +
                      "?subject=" +
                      Uri.EscapeDataString(
                          $"Re: {subject} [{reference}]") +
                      "&body=" +
                      Uri.EscapeDataString(replyBody);

            return new AdminContactMessageDetailsViewModel
            {
                AuditLogId = log.AuditLogId,
                Reference = reference,
                Name = payload.Name ?? "Unknown",
                Email = email,
                Phone = payload.Phone,
                InquiryType =
                    FirstNonBlank(
                        payload.InquiryType,
                        "General"),
                Subject = subject,
                Message = payload.Message ?? "",
                Status =
                    NormalizeStatus(
                        workflow?.Status ?? "New"),
                CreatedAt = log.CreatedAt,

                TargetFarmerId =
                    payload.TargetFarmerId,
                TargetFarmerName =
                    payload.TargetFarmerName,

                AdminNote =
                    workflow?.AdminNote,
                StatusUpdatedAt =
                    workflow?.UpdatedAt,
                StatusUpdatedByUserId =
                    workflow?.UpdatedByUserId,

                SubmittedByUserId =
                    log.UserId,
                IpAddress =
                    log.IpAddress,
                UserAgent =
                    log.UserAgent,

                ReplyMailtoUrl =
                    mailto
            };
        }

        private static ContactPayload? ParsePayload(
            string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize<ContactPayload>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
            }
            catch
            {
                return null;
            }
        }

        private static ContactWorkflowState? ParseWorkflow(
            string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize<ContactWorkflowState>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeStatus(
            string? status)
        {
            if (string.Equals(
                    status,
                    "In Progress",
                    StringComparison.OrdinalIgnoreCase))
                return "In Progress";

            if (string.Equals(
                    status,
                    "Replied",
                    StringComparison.OrdinalIgnoreCase))
                return "Replied";

            if (string.Equals(
                    status,
                    "Resolved",
                    StringComparison.OrdinalIgnoreCase))
                return "Resolved";

            return "New";
        }

        private static bool Contains(
            string? source,
            string needle)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                   source.Contains(
                       needle,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildPreview(
            string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return "";

            var compact =
                message
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Trim();

            return compact.Length <= 105
                ? compact
                : compact[..105] + "…";
        }

        private static string FirstNonBlank(
            params string?[] values)
        {
            return values.FirstOrDefault(
                       x => !string.IsNullOrWhiteSpace(x))
                   ?.Trim()
                   ?? "";
        }

        private class ContactPayload
        {
            public string? Reference { get; set; }
            public string? Name { get; set; }
            public string? Email { get; set; }
            public string? Phone { get; set; }
            public string? InquiryType { get; set; }
            public string? Subject { get; set; }
            public string? Message { get; set; }
            public int? TargetFarmerId { get; set; }
            public string? TargetFarmerName { get; set; }
            public DateTime? SubmittedAtUtc { get; set; }
        }

        private class ContactWorkflowState
        {
            public string Status { get; set; } = "New";
            public string? AdminNote { get; set; }
            public DateTime? UpdatedAt { get; set; }
            public int? UpdatedByUserId { get; set; }
        }
    }
}

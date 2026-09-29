using System.Security.Claims;
using System.Text.Json;
using MarketLink.Models;
using MarketLink.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Controllers
{
    public class ContactController : Controller
    {
        private readonly ApplicationDbContext _context;

        private static readonly HashSet<string> AllowedInquiryTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "General",
                "Order Help",
                "Farmer Inquiry",
                "Market Help",
                "Account Help",
                "Technical Issue",
                "Partnership"
            };

        public ContactController(ApplicationDbContext context)
        {
            _context = context;
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> Index(
            int? farmerId = null,
            string? farmer = null,
            CancellationToken cancellationToken = default)
        {
            var model = new ContactPageViewModel();

            await PopulateDynamicDataAsync(
                model,
                cancellationToken);

            // Pre-fill logged-in user details.
            if (User.Identity?.IsAuthenticated == true &&
                int.TryParse(
                    User.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var userId))
            {
                var user = await _context.users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        u => u.UserId == userId,
                        cancellationToken);

                if (user != null)
                {
                    model.Name = user.FullName;
                    model.Email = user.Email;
                    model.Phone = user.Phone;
                }
            }

            // Optional farmer-specific inquiry.
            Farmer? targetFarmer = null;

            if (farmerId.HasValue)
            {
                targetFarmer = await _context.farmers
                    .AsNoTracking()
                    .Include(f => f.User)
                    .FirstOrDefaultAsync(
                        f =>
                            f.FarmerId == farmerId.Value &&
                            f.IsActive,
                        cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(farmer))
            {
                targetFarmer = await _context.farmers
                    .AsNoTracking()
                    .Include(f => f.User)
                    .FirstOrDefaultAsync(
                        f =>
                            f.BusinessName == farmer &&
                            f.IsActive,
                        cancellationToken);
            }

            if (targetFarmer != null)
            {
                model.TargetFarmerId =
                    targetFarmer.FarmerId;

                model.TargetFarmerName =
                    targetFarmer.BusinessName;

                model.InquiryType =
                    "Farmer Inquiry";

                model.Subject =
                    $"Question for {targetFarmer.BusinessName}";
            }

            model.SuccessMessage =
                TempData["ContactSuccess"] as string;

            model.ReferenceNumber =
                TempData["ContactReference"] as string;

            return View(model);
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(
            ContactPageViewModel model,
            CancellationToken cancellationToken)
        {
            await PopulateDynamicDataAsync(
                model,
                cancellationToken);

            if (!AllowedInquiryTypes.Contains(
                    model.InquiryType ?? ""))
            {
                ModelState.AddModelError(
                    nameof(model.InquiryType),
                    "Please select a valid inquiry type.");
            }

            Farmer? targetFarmer = null;

            if (model.TargetFarmerId.HasValue)
            {
                targetFarmer = await _context.farmers
                    .Include(f => f.User)
                    .FirstOrDefaultAsync(
                        f =>
                            f.FarmerId == model.TargetFarmerId.Value &&
                            f.IsActive,
                        cancellationToken);

                if (targetFarmer == null)
                {
                    ModelState.AddModelError(
                        "",
                        "The selected farmer is no longer available.");
                }
                else
                {
                    model.TargetFarmerName =
                        targetFarmer.BusinessName;
                }
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            int? currentUserId = null;

            if (User.Identity?.IsAuthenticated == true &&
                int.TryParse(
                    User.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var userId))
            {
                currentUserId = userId;
            }

            var reference =
                $"MLC-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}";

            // Persist the full website contact request without requiring
            // a new table/migration by using the existing AuditLog table.
            var contactPayload = new
            {
                Reference = reference,
                model.Name,
                model.Email,
                model.Phone,
                model.InquiryType,
                model.Subject,
                model.Message,
                model.TargetFarmerId,
                model.TargetFarmerName,
                SubmittedAtUtc = DateTime.UtcNow
            };

            _context.auditlogs.Add(
                new AuditLog
                {
                    UserId = currentUserId,
                    Action = "WebsiteContact",
                    EntityName = "ContactMessage",
                    EntityId = reference,
                    NewValuesJson =
                        JsonSerializer.Serialize(contactPayload),
                    IpAddress =
                        HttpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent =
                        Request.Headers.UserAgent.ToString(),
                    CreatedAt = DateTime.Now
                });

            var notificationMessage =
                BuildNotificationMessage(
                    model,
                    reference);

            if (targetFarmer != null)
            {
                _context.notifications.Add(
                    new Notification
                    {
                        UserId = targetFarmer.UserId,
                        Title =
                            $"New MarketLink inquiry from {model.Name}",
                        Message =
                            notificationMessage,
                        NotificationType =
                            "ContactInquiry",
                        ActionUrl = null,
                        IsRead = false,
                        CreatedAt = DateTime.Now
                    });
            }
            else
            {
                var adminUsers = await _context.users
                    .AsNoTracking()
                    .Include(u => u.Role)
                    .Where(u =>
                        u.IsActive &&
                        u.Role != null &&
                        u.Role.RoleName.ToLower() == "admin")
                    .Select(u => u.UserId)
                    .ToListAsync(cancellationToken);

                foreach (var adminUserId in adminUsers)
                {
                    _context.notifications.Add(
                        new Notification
                        {
                            UserId = adminUserId,
                            Title =
                                $"Website contact: {model.Subject}",
                            Message =
                                notificationMessage,
                            NotificationType =
                                "ContactInquiry",
                            ActionUrl = null,
                            IsRead = false,
                            CreatedAt = DateTime.Now
                        });
                }
            }

            await _context.SaveChangesAsync(
                cancellationToken);

            TempData["ContactSuccess"] =
                targetFarmer != null
                    ? $"Your message for {targetFarmer.BusinessName} was submitted successfully."
                    : "Your message was submitted successfully. The MarketLink team has been notified.";

            TempData["ContactReference"] =
                reference;

            return RedirectToAction(
                nameof(Index),
                model.TargetFarmerId.HasValue
                    ? new { farmerId = model.TargetFarmerId.Value }
                    : null);
        }

        private async Task PopulateDynamicDataAsync(
            ContactPageViewModel model,
            CancellationToken cancellationToken)
        {
            var settings = await _context.systemsettings
                .AsNoTracking()
                .Where(s => s.IsPublic)
                .ToDictionaryAsync(
                    s => s.SettingKey,
                    s => s.SettingValue,
                    StringComparer.OrdinalIgnoreCase,
                    cancellationToken);

            model.Location =
                GetSetting(
                    settings,
                    "Contact.Location",
                    "Karachi, Sindh, Pakistan");

            model.SupportEmail =
                GetSetting(
                    settings,
                    "Contact.Email",
                    "hello@marketlink.com");

            model.PhoneNumber =
                GetSetting(
                    settings,
                    "Contact.Phone",
                    "+92 300 1234567");

            model.SupportHours =
                GetSetting(
                    settings,
                    "Contact.Hours",
                    "Mon - Sat, 09:00 AM - 06:00 PM");

            model.FacebookUrl =
                GetOptionalSetting(
                    settings,
                    "Contact.Facebook");

            model.InstagramUrl =
                GetOptionalSetting(
                    settings,
                    "Contact.Instagram");

            model.TwitterUrl =
                GetOptionalSetting(
                    settings,
                    "Contact.Twitter");

            model.LinkedInUrl =
                GetOptionalSetting(
                    settings,
                    "Contact.LinkedIn");

            model.ActiveMarketCount =
                await _context.markets
                    .AsNoTracking()
                    .CountAsync(
                        m => m.IsActive,
                        cancellationToken);

            model.AvailableProductCount =
                await _context.farmerproducts
                    .AsNoTracking()
                    .CountAsync(
                        fp =>
                            fp.IsActive &&
                            fp.IsApproved &&
                            fp.IsAvailable,
                        cancellationToken);

            model.ActiveFarmerCount =
                await _context.farmers
                    .AsNoTracking()
                    .CountAsync(
                        f =>
                            f.IsActive &&
                            f.IsApproved,
                        cancellationToken);
        }

        private static string BuildNotificationMessage(
            ContactPageViewModel model,
            string reference)
        {
            var phone =
                string.IsNullOrWhiteSpace(model.Phone)
                    ? "Not provided"
                    : model.Phone.Trim();

            return
                $"Reference: {reference}\n" +
                $"Type: {model.InquiryType}\n" +
                $"From: {model.Name} ({model.Email})\n" +
                $"Phone: {phone}\n" +
                $"Subject: {model.Subject}\n\n" +
                model.Message.Trim();
        }

        private static string GetSetting(
            IReadOnlyDictionary<string, string> settings,
            string key,
            string fallback)
        {
            return settings.TryGetValue(
                       key,
                       out var value) &&
                   !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : fallback;
        }

        private static string? GetOptionalSetting(
            IReadOnlyDictionary<string, string> settings,
            string key)
        {
            return settings.TryGetValue(
                       key,
                       out var value) &&
                   !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null;
        }
    }
}

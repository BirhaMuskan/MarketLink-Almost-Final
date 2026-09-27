using System.Security.Claims;
using System.Text.Json;
using MarketLink.Models;
using MarketLink.Services;
using MarketLink.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Controllers
{
    [Authorize(Roles = "Customer")]
    public class CustomerAssistantController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IMarketLinkAssistantService _assistantService;

        public CustomerAssistantController(
            ApplicationDbContext context,
            IMarketLinkAssistantService assistantService)
        {
            _context = context;
            _assistantService = assistantService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            int? conversationId,
            CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();

            if (!userId.HasValue)
                return Forbid();

            var model = await BuildPageAsync(
                userId.Value,
                conversationId,
                cancellationToken);

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ask(
            CustomerAssistantPageViewModel input,
            CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();

            if (!userId.HasValue)
                return Forbid();

            if (string.IsNullOrWhiteSpace(input.Question))
            {
                TempData["AssistantError"] =
                    "Type a question for the MarketLink Assistant.";

                return RedirectToAction(
                    nameof(Index),
                    new { conversationId = input.ConversationId });
            }

            var conversation = await GetOrCreateConversationAsync(
                userId.Value,
                input.ConversationId,
                input.Question,
                cancellationToken);

            _context.assistantmessages.Add(
                new AssistantMessage
                {
                    AssistantConversationId =
                        conversation.AssistantConversationId,
                    MessageRole = "user",
                    MessageText = input.Question.Trim(),
                    CreatedAt = DateTime.Now
                });

            conversation.LastActivityAt =
                DateTime.Now;

            await _context.SaveChangesAsync(
                cancellationToken);

            var reply = await _assistantService.AskAsync(
                userId.Value,
                conversation.AssistantConversationId,
                input.Question,
                cancellationToken);

            _context.assistantmessages.Add(
                new AssistantMessage
                {
                    AssistantConversationId =
                        conversation.AssistantConversationId,
                    MessageRole = "assistant",
                    MessageText = reply.Text,
                    MetadataJson = JsonSerializer.Serialize(
                        new
                        {
                            intent = reply.Intent,
                            metadata = reply.Metadata,
                            actions = reply.Actions
                        }),
                    CreatedAt = DateTime.Now
                });

            conversation.LastActivityAt =
                DateTime.Now;

            await _context.SaveChangesAsync(
                cancellationToken);

            return RedirectToAction(
                nameof(Index),
                new
                {
                    conversationId =
                        conversation.AssistantConversationId
                });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> NewConversation(
            CancellationToken cancellationToken)
        {
            var userId = GetCurrentUserId();

            if (!userId.HasValue)
                return Forbid();

            var existing = await _context.assistantconversations
                .Where(c =>
                    c.UserId == userId.Value &&
                    c.IsActive)
                .ToListAsync(cancellationToken);

            foreach (var conversation in existing)
            {
                conversation.IsActive = false;
            }

            await _context.SaveChangesAsync(
                cancellationToken);

            return RedirectToAction(nameof(Index));
        }

        private async Task<CustomerAssistantPageViewModel> BuildPageAsync(
            int userId,
            int? conversationId,
            CancellationToken cancellationToken)
        {
            var conversations = await _context.assistantconversations
                .AsNoTracking()
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.LastActivityAt)
                .Take(20)
                .ToListAsync(cancellationToken);

            AssistantConversation? current = null;

            if (conversationId.HasValue)
            {
                current = conversations
                    .FirstOrDefault(c =>
                        c.AssistantConversationId ==
                        conversationId.Value);
            }

            current ??= conversations
                .FirstOrDefault(c => c.IsActive);

            var model =
                new CustomerAssistantPageViewModel
                {
                    ConversationId =
                        current?.AssistantConversationId,
                    Conversations = conversations
                        .Select(c =>
                            new CustomerAssistantConversationViewModel
                            {
                                AssistantConversationId =
                                    c.AssistantConversationId,
                                Title = string.IsNullOrWhiteSpace(c.Title)
                                    ? "MarketLink Assistant"
                                    : c.Title,
                                LastActivityAt =
                                    c.LastActivityAt
                            })
                        .ToList()
                };

            if (current == null)
                return model;

            var messages = await _context.assistantmessages
                .AsNoTracking()
                .Where(m =>
                    m.AssistantConversationId ==
                    current.AssistantConversationId)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync(cancellationToken);

            model.Messages = messages
                .Select(ToMessageViewModel)
                .ToList();

            return model;
        }

        private async Task<AssistantConversation> GetOrCreateConversationAsync(
            int userId,
            int? conversationId,
            string firstQuestion,
            CancellationToken cancellationToken)
        {
            if (conversationId.HasValue)
            {
                var existing =
                    await _context.assistantconversations
                        .FirstOrDefaultAsync(
                            c =>
                                c.AssistantConversationId ==
                                conversationId.Value &&
                                c.UserId == userId,
                            cancellationToken);

                if (existing != null)
                {
                    existing.IsActive = true;
                    return existing;
                }
            }

            var active =
                await _context.assistantconversations
                    .FirstOrDefaultAsync(
                        c =>
                            c.UserId == userId &&
                            c.IsActive,
                        cancellationToken);

            if (active != null)
                return active;

            var title = firstQuestion.Trim();

            if (title.Length > 70)
                title = title[..70] + "...";

            var conversation =
                new AssistantConversation
                {
                    UserId = userId,
                    Title = title,
                    StartedAt = DateTime.Now,
                    LastActivityAt = DateTime.Now,
                    IsActive = true
                };

            _context.assistantconversations.Add(
                conversation);

            await _context.SaveChangesAsync(
                cancellationToken);

            return conversation;
        }

        private static CustomerAssistantMessageViewModel ToMessageViewModel(
            AssistantMessage message)
        {
            var vm =
                new CustomerAssistantMessageViewModel
                {
                    AssistantMessageId =
                        message.AssistantMessageId,
                    Role =
                        message.MessageRole,
                    Text =
                        message.MessageText,
                    CreatedAt =
                        message.CreatedAt
                };

            if (string.IsNullOrWhiteSpace(
                    message.MetadataJson))
            {
                return vm;
            }

            try
            {
                using var doc =
                    JsonDocument.Parse(
                        message.MetadataJson);

                if (doc.RootElement.TryGetProperty(
                        "actions",
                        out var actions)
                    &&
                    actions.ValueKind ==
                    JsonValueKind.Array)
                {
                    foreach (var action in
                             actions.EnumerateArray())
                    {
                        vm.Actions.Add(
                            new CustomerAssistantActionViewModel
                            {
                                Label =
                                    ReadString(
                                        action,
                                        "Label",
                                        "label")
                                    ?? "Open",
                                Controller =
                                    ReadString(
                                        action,
                                        "Controller",
                                        "controller")
                                    ?? "Home",
                                Action =
                                    ReadString(
                                        action,
                                        "Action",
                                        "action")
                                    ?? "Index",
                                Id =
                                    ReadNullableInt(
                                        action,
                                        "Id",
                                        "id")
                            });
                    }
                }
            }
            catch
            {
                // Old/malformed metadata should not break the chat page.
            }

            return vm;
        }

        private static string? ReadString(
            JsonElement element,
            string name1,
            string name2)
        {
            if (element.TryGetProperty(
                    name1,
                    out var p1)
                &&
                p1.ValueKind ==
                JsonValueKind.String)
            {
                return p1.GetString();
            }

            if (element.TryGetProperty(
                    name2,
                    out var p2)
                &&
                p2.ValueKind ==
                JsonValueKind.String)
            {
                return p2.GetString();
            }

            return null;
        }

        private static int? ReadNullableInt(
            JsonElement element,
            string name1,
            string name2)
        {
            if (element.TryGetProperty(
                    name1,
                    out var p1)
                &&
                p1.ValueKind ==
                JsonValueKind.Number
                &&
                p1.TryGetInt32(out var v1))
            {
                return v1;
            }

            if (element.TryGetProperty(
                    name2,
                    out var p2)
                &&
                p2.ValueKind ==
                JsonValueKind.Number
                &&
                p2.TryGetInt32(out var v2))
            {
                return v2;
            }

            return null;
        }

        private int? GetCurrentUserId()
        {
            var value =
                User.FindFirstValue(
                    ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub");

            return int.TryParse(
                value,
                out var userId)
                ? userId
                : null;
        }
    }
}

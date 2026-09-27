using System.Security.Claims;
using System.Text.Json;
using MarketLink.Models;
using MarketLink.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLink.Controllers
{
    [AllowAnonymous]
    public class AssistantWidgetController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IMarketLinkAssistantService _assistantService;

        public AssistantWidgetController(
            ApplicationDbContext context,
            IMarketLinkAssistantService assistantService)
        {
            _context = context;
            _assistantService = assistantService;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ask(
            string question,
            CancellationToken cancellationToken)
        {
            question = (question ?? "").Trim();

            if (string.IsNullOrWhiteSpace(question))
            {
                return Json(new
                {
                    ok = false,
                    message = "Type a question first."
                });
            }

            if (question.Length > 500)
            {
                question = question[..500];
            }

            int? userId = null;
            int? conversationId = null;

            // Customer accounts receive personalized answers and their widget
            // chat is synchronized with the full Assistant page.
            if (User.Identity?.IsAuthenticated == true &&
                User.IsInRole("Customer"))
            {
                var claim =
                    User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? User.FindFirstValue("sub");

                if (int.TryParse(claim, out var parsedUserId))
                {
                    userId = parsedUserId;

                    var conversation =
                        await GetOrCreateConversationAsync(
                            parsedUserId,
                            question,
                            cancellationToken);

                    conversationId =
                        conversation.AssistantConversationId;

                    _context.assistantmessages.Add(
                        new AssistantMessage
                        {
                            AssistantConversationId =
                                conversation.AssistantConversationId,
                            MessageRole = "user",
                            MessageText = question,
                            CreatedAt = DateTime.Now
                        });

                    conversation.LastActivityAt = DateTime.Now;

                    await _context.SaveChangesAsync(
                        cancellationToken);
                }
            }

            var reply = await _assistantService.AskAsync(
                userId,
                conversationId,
                question,
                cancellationToken);

            if (userId.HasValue &&
                conversationId.HasValue)
            {
                _context.assistantmessages.Add(
                    new AssistantMessage
                    {
                        AssistantConversationId =
                            conversationId.Value,
                        MessageRole = "assistant",
                        MessageText = reply.Text,
                        MetadataJson =
                            JsonSerializer.Serialize(
                                new
                                {
                                    intent = reply.Intent,
                                    metadata = reply.Metadata,
                                    actions = reply.Actions
                                }),
                        CreatedAt = DateTime.Now
                    });

                var conversation =
                    await _context.assistantconversations
                        .FirstAsync(
                            c =>
                                c.AssistantConversationId ==
                                conversationId.Value,
                            cancellationToken);

                conversation.LastActivityAt = DateTime.Now;

                await _context.SaveChangesAsync(
                    cancellationToken);
            }

            var actions = reply.Actions
                .Select(a => new
                {
                    label = a.Label,
                    url = Url.Action(
                        a.Action,
                        a.Controller,
                        a.Id.HasValue
                            ? new { id = a.Id.Value }
                            : null)
                })
                .Where(a => !string.IsNullOrWhiteSpace(a.url))
                .ToList();

            return Json(new
            {
                ok = true,
                message = reply.Text,
                intent = reply.Intent,
                personalized = userId.HasValue,
                actions,
                fullAssistantUrl =
                    userId.HasValue
                        ? Url.Action(
                            "Index",
                            "CustomerAssistant",
                            new
                            {
                                conversationId
                            })
                        : Url.Action(
                            "Index",
                            "Auth")
            });
        }

        private async Task<AssistantConversation> GetOrCreateConversationAsync(
            int userId,
            string firstQuestion,
            CancellationToken cancellationToken)
        {
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
            {
                title = title[..70] + "...";
            }

            var conversation =
                new AssistantConversation
                {
                    UserId = userId,
                    Title = title,
                    StartedAt = DateTime.Now,
                    LastActivityAt = DateTime.Now,
                    IsActive = true
                };

            _context.assistantconversations.Add(conversation);

            await _context.SaveChangesAsync(cancellationToken);

            return conversation;
        }
    }
}

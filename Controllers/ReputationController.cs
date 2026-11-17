using Microsoft.AspNetCore.Mvc;
using product_website.Data;
using product_website.Models;
using product_website.Services;

namespace product_website.Controllers;

/// <summary>The app's trust card: GET /api/me/reputation</summary>
[Route("api/me/reputation")]
public class ReputationController : AuthedApiController
{
    private readonly ReputationRepository _reputation;

    public ReputationController(ReputationRepository reputation) => _reputation = reputation;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var summary = await _reputation.GetSummaryAsync(UserId);
        if (summary is null) return Unauthorized();

        var events = await _reputation.GetEventsAsync(UserId, 50);

        var unlocked = summary.Score >= ReputationRules.BadgesMinScore;

        return Ok(new ReputationResponse
        {
            Summary = summary,
            BadgesUnlocked = unlocked,
            BadgesHint = unlocked ? string.Empty : "Badges unlock at the Trusted level (70 points).",
            Badges = unlocked ? ReputationRules.BadgesFor(summary) : new List<BadgeDto>(),
            Events = events.Select(e => new ReputationEventDto
            {
                ReportId = e.ReportId,
                Kind = e.Kind,
                Label = ReputationRules.KindLabel(e.Kind),
                Delta = e.Delta,
                ScoreAfter = e.ScoreAfter,
                Note = e.Note,
                CreatedAt = e.CreatedAt
            }).ToList()
        });
    }
}

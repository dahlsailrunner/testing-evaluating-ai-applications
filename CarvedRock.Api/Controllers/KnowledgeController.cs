using CarvedRock.Core;
using CarvedRock.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarvedRock.Api.Controllers;

/// <summary>
/// Semantic search over the CarvedRock help documents (returns, shipping, warranty, gear
/// guides...). The documents are public information, so no sign-in is required.
/// </summary>
[ApiController]
[Route("[controller]")]
[AllowAnonymous]
public class KnowledgeController(IKnowledgeLogic knowledge) : ControllerBase
{
    /// <param name="q">What to look for, in plain language.</param>
    /// <param name="top">How many chunks to return (1 to 10).</param>
    [HttpGet("search")]
    [ProducesResponseType<KnowledgeSearchResponseModel>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Search([FromQuery] string? q,
        [FromQuery] int top = IKnowledgeLogic.DefaultTop,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Problem(detail: "Provide a search query in the 'q' parameter.",
                statusCode: StatusCodes.Status400BadRequest);

        if (q.Length > IKnowledgeLogic.MaxQueryLength)
            return Problem(detail: $"The query is too long (maximum {IKnowledgeLogic.MaxQueryLength} characters).",
                statusCode: StatusCodes.Status400BadRequest);

        try
        {
            var results = await knowledge.SearchAsync(q, top, cancellationToken);
            return Ok(new KnowledgeSearchResponseModel { Query = q.Trim(), Results = results });
        }
        catch (KnowledgeUnavailableException)
        {
            return Problem(title: "Knowledge search is unavailable",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}

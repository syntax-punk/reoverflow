using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestionService.Data;
using QuestionService.DTOs;
using QuestionService.Models;

namespace QuestionService.Controllers;

[ApiController]
[Route("[controller]")]
public class QuestionsController(QuestionDbContext db) : ControllerBase
{
    [Authorize]
    [HttpPost]
    public async Task<ActionResult<Question>> CreateQuestion(CreateQuestionDto dto)
    {
        var validTags = await db.Tags.Where(t => dto.Tags.Contains(t.Slug)).ToListAsync();
        var missing = dto.Tags.Except(validTags.Select(t => t.Slug).ToList()).ToList();
        if (missing.Count != 0)
            return BadRequest($"Invalid tags: {string.Join(", ", missing)}");
        
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = User.FindFirstValue("name");

        if (userId is null || name is null) return BadRequest("Cannot get user details");

        var question = new Question
        {
            Title = dto.Title,
            Content = dto.Content,
            TagSlugs = dto.Tags,
            AskerId = userId,
            AskerDisplayName = name
        };
        
        db.Questions.Add(question);
        await db.SaveChangesAsync();
        
        return Created($"questions/{question.Id}", question);
    }
}
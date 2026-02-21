using System.Security.Claims;
using Contracts;
using FastExpressionCompiler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestionService.Data;
using QuestionService.DTOs;
using QuestionService.Models;
using QuestionService.services;
using Wolverine;

namespace QuestionService.Controllers;

[ApiController]
[Route("[controller]")]
public class QuestionsController(QuestionDbContext db, IMessageBus bus, TagService tagService) : ControllerBase
{
    [Authorize]
    [HttpPost]
    public async Task<ActionResult<Question>> CreateQuestion(CreateQuestionDto dto)
    {
        if (!await tagService.AreTagsValidAsync(dto.Tags))
            return BadRequest("Invalid tags");
        
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
        
        await bus.PublishAsync(new QuestionCreated(question.Id, question.Title, question.Content, 
            question.CreatedAt, question.TagSlugs));
        
        return Created($"questions/{question.Id}", question);
    }

    [HttpGet]
    public async Task<ActionResult<List<Question>>> GetQuestions(string? tag)
    {
        var query = db.Questions.AsQueryable();
        
        if (!string.IsNullOrEmpty(tag))
        {
            query = query.Where(q => q.TagSlugs.Contains(tag));
        }
        
        return await query.OrderByDescending(q => q.CreatedAt).ToListAsync();
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Question>> GetQuestion(string id)
    {
        var question = await db.Questions.FindAsync(id);
        if (question is null) return NotFound();

        await db.Questions.Where(q => q.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ViewCount, x => x.ViewCount + 1));
        
        return question;
    }

    [Authorize]
    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateQuestion(string id, CreateQuestionDto dto)
    {
        var question = await db.Questions
            .Include(q => q.Answers)
            .FirstOrDefaultAsync(q =>  q.Id == id);
        if (question is null) return NotFound();
        
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId != question.AskerId) return Forbid();
        
        if (!await tagService.AreTagsValidAsync(dto.Tags))
            return BadRequest("Invalid tags");
        
        question.Title = dto.Title;
        question.Content = dto.Content;
        question.TagSlugs = dto.Tags;
        question.UpdatedAt  = DateTime.UtcNow;

        await db.SaveChangesAsync();
        await bus.PublishAsync(new QuestionUpdated(question.Id, question.Title, question.Content,
            question.TagSlugs.AsArray()));
        
        return NoContent();
    }

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteQuestion(string id)
    {
        var question = await db.Questions.FindAsync(id);
        if (question is null) return NotFound();
        
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId != question.AskerId) return Forbid();
        
        db.Questions.Remove(question);
        await db.SaveChangesAsync();
        await bus.PublishAsync(new QuestionDeleted(question.Id));
        
        return NoContent();
    }

    [Authorize]
    [HttpPost("{questionId}/answers")]
    public async Task<ActionResult<Answer>> CreateAnswer(string questionId, CreateAnswerDto dto)
    {
        var question = await db.Questions
            .Include(q => q.Answers)
            .FirstOrDefaultAsync(q => q.Id.Equals(questionId));
        if (question is null) return NotFound();
        
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var name = User.FindFirstValue("name");

        if (userId is null || name is null) return BadRequest("Cannot get user details");

        var answer = new Answer()
        {
            Content = dto.Content,
            QuestionId = question.Id,
            Question =  question,
            UserDisplayName =  name,
            UserId = userId
        };
        
        question.Answers.Add(answer);
        question.AnswerCount = question.Answers.Count;
        
        db.Answers.Add(answer);
        await db.SaveChangesAsync();
        await bus.PublishAsync(new AnswerCountUpdated(question.Id, question.Answers.Count));
        
        return Created($"questions/{question.Id}", answer);
    }

    [Authorize]
    [HttpPut("{questionId}/answers/{answerId}")]
    public async Task<ActionResult> UpdateAnswer(string questionId, string answerId, CreateAnswerDto dto)
    {
        var question = await db.Questions
            .Include(q => q.Answers)
            .FirstOrDefaultAsync(q => q.Id.Equals(questionId));
        if (question is null) return NotFound();
        
        var answer = await db.Answers.FindAsync(answerId);
        if (answer is null) return NotFound();
        if (answer.QuestionId != questionId) return Forbid();
        
        answer.Content = dto.Content;
        answer.UpdatedAt = DateTime.UtcNow;
        var questionAnswer = question.Answers.Find(a => a.Id.Equals(answerId));
        questionAnswer?.Content = answer.Content;

        await db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize]
    [HttpDelete("{questionId}/answers/{answerId}")]
    public async Task<ActionResult> DeleteAnswer(string questionId, string answerId)
    {
        var question = await db.Questions
            .Include(q => q.Answers)
            .FirstOrDefaultAsync(q => q.Id.Equals(questionId));
        if (question is null) return NotFound();
        
        var answer = await db.Answers.FindAsync(answerId);
        if (answer is null) return NotFound();
        if (answer.QuestionId != questionId) return Forbid();
        if (answer.Accepted) return BadRequest("You cannot delete accepted answer");
        
        db.Answers.Remove(answer);
        question.Answers.Remove(answer);
        
        await db.SaveChangesAsync();
        await bus.PublishAsync(new AnswerCountUpdated(question.Id, question.Answers.Count));
        return NoContent();
    }

    [Authorize]
    [HttpPost("{questionId}/answers/{answerId}/accept")]
    public async Task<ActionResult> AcceptAnswer(string questionId, string answerId)
    {
        var question = await db.Questions
            .Include(q => q.Answers)
            .FirstOrDefaultAsync(q => q.Id.Equals(questionId));
        if (question is null) return NotFound();
        
        var answer = await db.Answers.FindAsync(answerId);
        if (answer is null) return NotFound();
        if (answer.QuestionId != questionId) return Forbid();
        if (answer.Accepted) return BadRequest("This answer has already been accepted");
        if (question.Answers.Any(a => a.Accepted))
            return BadRequest("This question already has an accepted answer");
        
        answer.Accepted = true;
        var questionAnswer = question.Answers.Find(a => a.Id.Equals(answerId));
        questionAnswer?.Accepted = true;

        await db.SaveChangesAsync();
        await bus.PublishAsync(new AnswerAccepted(questionId));
        return NoContent();
    }
}
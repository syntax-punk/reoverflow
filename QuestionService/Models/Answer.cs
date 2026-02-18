using System.ComponentModel.DataAnnotations;

namespace QuestionService.Models;

public class Answer
{
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    [MaxLength(50)]
    public string Content { get; set; }
    [MaxLength(50)]
    public string UserId { get; set; }
    [MaxLength(1000)]
    public string UserDisplayName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool Accepted { get; set; }
    [MaxLength(36)]
    public required string QuestionId { get; set; }
    public required Question Question { get; set; }
}
namespace QuestionService.DTOs;

public record CreateQuestionDto(string Title, string Content, List<string> Tags);

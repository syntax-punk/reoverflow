using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QuestionService.Data;
using QuestionService.Models;

namespace QuestionService.services;

public class TagService(IMemoryCache cache, QuestionDbContext db)
{
    private const string CacheKey = "tags";

    private async Task<List<Tag>> getTags()
    {
        return await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(2);
            var tags = db.Tags.AsNoTracking().ToList();
            
            return tags;
        }) ?? [];
    }

    public async Task<bool> AreTagsValidAsync(List<string> slugs)
    {
        var tags = await getTags();
        var tagSet = tags.Select(t => t.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return slugs.All(s => tagSet.Contains(s));
    }
}
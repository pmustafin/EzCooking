using System.Text.Json;
using EzCooking.Api.Data;
using EzCooking.Api.Recipes;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EzCooking.Api.Translation;

/// <summary>Requests a Russian translation for a saved recipe version.</summary>
public sealed record RequestRussianTranslation(int Version, bool Consent);

/// <summary>Translation state and, when ready, the translated recipe.</summary>
public sealed record RussianTranslationResponse(int Version, string Status, RecipeDocument? Document, string? Error);

public sealed class RussianTranslationService(NpgsqlDataSource db)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly DbContextOptions<EzCookingDbContext> _options = new DbContextOptionsBuilder<EzCookingDbContext>().UseNpgsql(db).Options;

    public async Task<RussianTranslationResponse?> Get(Guid id, CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_options);
        var result = await (from recipe in context.Recipes.AsNoTracking()
                            join translation in context.RecipeTranslations.AsNoTracking()
                                on new
                                {
                                    recipe.Id,
                                    recipe.Version
                                } equals new
                                {
                                    Id = translation.RecipeId,
                                    Version = translation.RecipeVersion
                                }
                            where recipe.Id == id && recipe.Status == "complete"
                            select translation).SingleOrDefaultAsync(ct);
        return result is null ? null : new(result.RecipeVersion, result.Status,
            result.Document is null ? null : JsonSerializer.Deserialize<RecipeDocument>(result.Document, _json), result.Error);
    }

    public async Task<bool> Queue(Guid id, int version, CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_options);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var recipes = await context.Recipes.FromSqlInterpolated($"""
            SELECT * FROM recipes WHERE id={id} FOR UPDATE
            """).AsNoTracking().ToListAsync(ct);
        var recipe = recipes.SingleOrDefault();
        if (recipe is null || recipe.Status != "complete" || recipe.Version != version || recipe.Document is null)
        {
            return false;
        }

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO recipe_translations(recipe_id,recipe_version,status,updated_at)
            VALUES ({id},{version},'queued',now())
            ON CONFLICT (recipe_id) DO UPDATE SET recipe_version=EXCLUDED.recipe_version,
                status='queued',document=NULL,error=NULL,updated_at=now()
            WHERE recipe_translations.recipe_version <> EXCLUDED.recipe_version
                OR recipe_translations.status='failed'
            """, ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}

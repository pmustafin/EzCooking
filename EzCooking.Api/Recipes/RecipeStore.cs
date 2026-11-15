using System.Text.Json;
using EzCooking.Api.Data;
using EzCooking.Api.Processing;
using EzCooking.RecipeValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EzCooking.Api.Recipes;

/// <summary>Recipe and processing status returned by the API.</summary>
public sealed record RecipeResponse(Guid Id, string? SourceUrl, string Status, string? Error,
    string? Document, int Version, DateTimeOffset CreatedAt, IReadOnlyList<string> Warnings, bool AudioConsent,
    IReadOnlyList<ProcessingEvent>? Activity = null);

/// <summary>A compact saved recipe for collection browsing.</summary>
public sealed record RecipeSummary(Guid Id, string Title, string Cover, IReadOnlyList<string> Tags, bool NeedsReview);
/// <summary>A page of results with an indication that more results exist.</summary>
public sealed record RecipePage<T>(IReadOnlyList<T> Items, bool HasMore);
public sealed class RecipeStore(NpgsqlDataSource db) : IRecipeStore
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly DbContextOptions<EzCookingDbContext> _contextOptions = new DbContextOptionsBuilder<EzCookingDbContext>().UseNpgsql(db).Options;
    public async Task Initialize(CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var tracking = new NpgsqlCommand("CREATE TABLE IF NOT EXISTS schema_migrations (version integer PRIMARY KEY)", connection, transaction))
        {
            await tracking.ExecuteNonQueryAsync(ct);
        }

        foreach (var (version, file) in new[] { (1, "001_initial.sql"), (2, "002_audio_consent.sql"), (3, "003_processing_views.sql"), (4, "004_duplicate_uploads.sql"), (5, "005_telegram.sql") })
        {
            await using var check = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE version=@version)", connection, transaction);
            check.Parameters.AddWithValue("version", version);
            if (!(bool)(await check.ExecuteScalarAsync(ct))!)
            {
                await using var stream = typeof(RecipeStore).Assembly.GetManifestResourceStream($"EzCooking.Api.Migrations.{file}")
                    ?? throw new InvalidOperationException($"Database migration {version} is missing.");
                using var reader = new StreamReader(stream);
                await using var migration = new NpgsqlCommand(await reader.ReadToEndAsync(ct), connection, transaction);
                await migration.ExecuteNonQueryAsync(ct);
                await using var mark = new NpgsqlCommand("INSERT INTO schema_migrations (version) VALUES (@version)", connection, transaction);
                mark.Parameters.AddWithValue("version", version);
                await mark.ExecuteNonQueryAsync(ct);
            }
        }
        await using var cmd = new NpgsqlCommand("""
            UPDATE recipes SET status='failed', error='Processing was interrupted. Retry this recipe or upload the video again.'
            WHERE status IN ('processing','staging');
            UPDATE recipes SET status='delete_failed', dismissed=false,
                error='Deletion was interrupted. Try deleting this item again.'
            WHERE status='deleting';
            """, connection, transaction);
        await cmd.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }
    private static RecipeResponse Read(RecipeEntity row)
    {
        var document = row.Document;
        var warnings = new List<string>();
        if (document is not null)
        {
            var recipe = JsonSerializer.Deserialize<RecipeDocument>(document, _json);
            if (recipe?.Ingredients is null || recipe.Steps is null)
            {
                warnings.Add("The saved recipe is incomplete.");
            }
            else
            {
                var draft = new RecipeDraft(recipe.Title,
                    recipe.Ingredients.Select(i => new IngredientDraft(i.Name, i.EstimatedAmount, null)).ToArray(),
                    recipe.Steps.Select(s => new StepDraft(s.Text, s.TimestampSeconds, null)).ToArray(),
                    recipe.Uncertainties);
                warnings.AddRange(RecipeValidator.Validate(draft, double.MaxValue, allowCookingMeasures: true,
                    sourceInstructions: recipe.RecipeSource is "caption" or "edited").Errors);
                if (recipe.RecipeSource != "caption")
                {
                    warnings.AddRange(RecipeValidator.FindTitleTagConflicts(recipe.Title,
                        recipe.Ingredients.Select(i => i.Name), recipe.Tags));
                }

                if (recipe.RecipeSource == "caption" && recipe.SourceCaption is not null &&
                    CaptionInstructions.HasIngredientQuantities(recipe.SourceCaption) &&
                    recipe.Ingredients.All(i => i.EstimatedAmount is null))
                {
                    warnings.Add("The caption contains quantities that were not extracted; check the original.");
                }

                if (recipe.RecipeSource == "caption" && recipe.SourceCaption is not null &&
                    recipe.Ingredients.Any(i => i.EstimatedAmount is not null &&
                        (!RecipeValidator.IsUsableAmount(i.EstimatedAmount) ||
                         CaptionInstructions.AssociatedAmount(recipe.SourceCaption, i.Name, i.EstimatedAmount) is null)))
                {
                    warnings.Add("Some ingredient amounts could not be matched reliably to the caption; check the original.");
                }
            }
        }
        return new(row.Id, row.SourceUrl, row.Status, row.Error, document,
            row.Version, row.CreatedAt, warnings, row.AudioConsent);
    }
    public async Task<RecipeResponse> Create(string? url, bool audioConsent, CancellationToken ct)
        => await CreateUnique(url, null, audioConsent, ct);

    public async Task<RecipeResponse> CreateUnique(string? url, string? uploadHash, bool audioConsent, CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var item = await CreateUnique(url, uploadHash, audioConsent, "staging", context, ct);
        await transaction.CommitAsync(ct);
        return item;
    }

    public async Task<RecipeResponse> QueueReel(string url, bool audioConsent, CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        await using var transaction = await context.Database.BeginTransactionAsync(ct);
        var item = await QueueReel(url, audioConsent, context, ct);
        await transaction.CommitAsync(ct);
        return item;
    }

    public Task<RecipeResponse> QueueReel(string url, bool audioConsent, EzCookingDbContext context, CancellationToken ct)
        => CreateUnique(url, null, audioConsent, "queued", context, ct);

    private static async Task<RecipeResponse> CreateUnique(string? url, string? uploadHash, bool audioConsent,
        string status, EzCookingDbContext context, CancellationToken ct)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Recipe creation requires a transaction.");
        }

        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1287583294)", ct);
        if (await context.Recipes.AnyAsync(recipe =>
            (url != null && recipe.SourceUrl == url) || (uploadHash != null && recipe.UploadSha256 == uploadHash), ct))
        {
            throw new DuplicateRecipeException();
        }
        var item = new RecipeEntity
        {
            Id = Guid.NewGuid(),
            SourceUrl = url,
            Status = status,
            AudioConsent = audioConsent,
            UploadSha256 = uploadHash
        };
        context.Recipes.Add(item);
        await context.SaveChangesAsync(ct);
        return Read(item);
    }
    public async Task<IReadOnlyList<Guid>> UploadsWithoutHash(CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        return await context.Recipes.AsNoTracking().Where(recipe => recipe.UploadSha256 == null)
            .Select(recipe => recipe.Id).ToListAsync(ct);
    }
    public async Task SetUploadHash(Guid id, string hash, CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        await context.Recipes.Where(recipe => recipe.Id == id && recipe.UploadSha256 == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(recipe => recipe.UploadSha256, hash), ct);
    }
    public async Task<bool> BeginDelete(Guid id, CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        return await context.Recipes.Where(recipe => recipe.Id == id &&
                (recipe.Status == "failed" || recipe.Status == "complete" || recipe.Status == "delete_failed"))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(recipe => recipe.Status, "deleting")
                .SetProperty(recipe => recipe.Dismissed, false)
                .SetProperty(recipe => recipe.Error, (string?)null), ct) == 1;
    }
    public async Task FinishDelete(Guid id, CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        if (await context.Recipes.Where(recipe => recipe.Id == id && recipe.Status == "deleting")
            .ExecuteDeleteAsync(ct) != 1)
        {
            throw new InvalidOperationException("The item changed during deletion.");
        }
    }
    public async Task<RecipeResponse?> Get(Guid id, CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        var row = await context.Recipes.AsNoTracking().SingleOrDefaultAsync(recipe => recipe.Id == id, ct);
        return row is null ? null : Read(row);
    }
    public async Task<List<RecipeResponse>> List(string? search, CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        var term = search?.Trim() ?? "";
        var rows = await context.Recipes.FromSqlInterpolated($"""
            SELECT * FROM recipes WHERE {term}='' OR document::text ILIKE '%' || {term} || '%' ORDER BY created_at DESC
            """).AsNoTracking().ToListAsync(ct);
        return rows.Select(Read).ToList();
    }
    public async Task<RecipePage<RecipeSummary>> Collection(string? search, int offset, int limit, CancellationToken ct, string? tag = null)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        var term = search?.Trim() ?? "";
        var filter = tag?.Trim() ?? "";
        var rows = await context.Recipes.FromSqlInterpolated($"""
            SELECT * FROM recipes
            WHERE status='complete' AND ({term}='' OR
                position(lower({term}) in lower(coalesce(document->>'title',''))) > 0 OR
                position(lower({term}) in lower(coalesce((document->'ingredients')::text,''))) > 0 OR
                position(lower({term}) in lower(coalesce((document->'tags')::text,''))) > 0)
            AND ({filter}='' OR EXISTS (
                SELECT 1 FROM jsonb_array_elements_text(
                    CASE WHEN jsonb_typeof(document->'tags')='array' THEN document->'tags' ELSE '[]'::jsonb END
                ) AS item(value) WHERE lower(trim(item.value))=lower({filter})))
            ORDER BY created_at DESC, id DESC OFFSET {offset} LIMIT {limit + 1}
            """).AsNoTracking().ToListAsync(ct);
        var items = new List<RecipeSummary>();
        foreach (var entity in rows)
        {
            var row = Read(entity);
            var document = row.Document is null ? null : JsonSerializer.Deserialize<RecipeDocument>(row.Document, _json);
            items.Add(new(row.Id, document?.Title ?? "Incomplete recipe", document?.Cover ?? "",
                document?.Tags ?? [], row.Warnings.Count > 0));
        }
        var more = items.Count > limit;
        if (more)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new(items, more);
    }
    public async Task<IReadOnlyList<string>> Tags(string? search, CancellationToken ct, bool popular = false)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        var term = search?.Trim() ?? "";
        return await context.Database.SqlQuery<string>($"""
            SELECT min(trim(item.value)) AS "Value"
            FROM recipes CROSS JOIN LATERAL jsonb_array_elements_text(
                CASE WHEN jsonb_typeof(document->'tags')='array' THEN document->'tags' ELSE '[]'::jsonb END
            ) AS item(value)
            WHERE status='complete' AND trim(item.value) <> ''
                AND position(lower({term}) in lower(trim(item.value))) > 0
            GROUP BY lower(trim(item.value))
            ORDER BY CASE WHEN {popular} THEN count(DISTINCT recipes.id) ELSE 0 END DESC,
                lower(min(trim(item.value))), min(trim(item.value))
            LIMIT 30
            """).ToListAsync(ct);
    }
    public async Task<RecipePage<RecipeResponse>> Processing(bool dismissed, int offset, int limit, CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        var rows = await context.Recipes.AsNoTracking()
            .Where(recipe => recipe.Status != "complete" && recipe.Dismissed == dismissed)
            .OrderByDescending(recipe => recipe.CreatedAt).ThenByDescending(recipe => recipe.Id)
            .Skip(offset).Take(limit + 1).ToListAsync(ct);
        var items = rows.Select(Read).ToList();

        var more = items.Count > limit;
        if (more)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new(items, more);
    }
    public async Task<bool> Dismiss(Guid id, bool dismissed, CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        return await context.Recipes.Where(recipe => recipe.Id == id && recipe.Status == "failed")
            .ExecuteUpdateAsync(setters => setters.SetProperty(recipe => recipe.Dismissed, dismissed), ct) == 1;
    }
    public async Task<RecipeResponse?> Claim(CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        var rows = await context.Recipes.FromSqlRaw("""
            UPDATE recipes SET status='processing', error=NULL
            WHERE id=(SELECT id FROM recipes WHERE status='queued' ORDER BY created_at
                      FOR UPDATE SKIP LOCKED LIMIT 1)
            RETURNING *
            """).AsNoTracking().ToListAsync(ct);
        return rows.Count == 0 ? null : Read(rows[0]);
    }
    public async Task SetStatus(Guid id, string status, string? error, CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        await context.Recipes.Where(recipe => recipe.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(recipe => recipe.Status, status)
                .SetProperty(recipe => recipe.Error, error), ct);
    }
    public async Task Complete(Guid id, string document, CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        if (await context.Recipes.Where(recipe => recipe.Id == id && recipe.Status == "processing" && recipe.Document == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(recipe => recipe.Status, "complete")
                .SetProperty(recipe => recipe.Document, document)
                .SetProperty(recipe => recipe.Version, recipe => recipe.Version + 1)
                .SetProperty(recipe => recipe.Error, (string?)null), ct) != 1)
        {
            throw new InvalidOperationException("Recipe changed during processing.");
        }
    }
    public async Task<bool> Edit(Guid id, int version, string document, CancellationToken ct)
    {
        using var parsed = JsonDocument.Parse(document);
        await using var context = new EzCookingDbContext(_contextOptions);
        return await context.Recipes.Where(recipe => recipe.Id == id && recipe.Version == version && recipe.Status == "complete")
            .ExecuteUpdateAsync(setters => setters.SetProperty(recipe => recipe.Document, document)
                .SetProperty(recipe => recipe.Version, recipe => recipe.Version + 1), ct) == 1;
    }
    public async Task<bool> Retry(Guid id, bool audioConsent, CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        return await context.Recipes.Where(recipe => recipe.Id == id && recipe.Status == "failed" && recipe.Document == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(recipe => recipe.Status, "queued")
                .SetProperty(recipe => recipe.Error, (string?)null)
                .SetProperty(recipe => recipe.AudioConsent, audioConsent)
                .SetProperty(recipe => recipe.Dismissed, false), ct) == 1;
    }
}

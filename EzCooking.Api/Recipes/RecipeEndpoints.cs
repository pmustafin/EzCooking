using System.Text.Json;
using System.Text.RegularExpressions;
using EzCooking.Api.Configuration;
using EzCooking.Api.Media;
using EzCooking.Api.Processing;
using EzCooking.Api.Translation;
using EzCooking.RecipeValidation;
using Npgsql;

namespace EzCooking.Api.Recipes;

/// <summary>Start a recipe from a public Instagram reel.</summary>
public sealed record CreateRecipeRequest(string Url, bool Consent, bool AudioConsent = false);
/// <summary>Confirm cloud analysis of a previously failed recipe.</summary>
public sealed record RetryRecipeRequest(bool Consent, bool AudioConsent = false);
/// <summary>Show or hide a failed processing job without deleting it.</summary>
public sealed record DismissRecipeRequest(bool Dismissed);
/// <summary>Correct a saved recipe without changing source information.</summary>
public sealed record EditRecipeRequest(int Version, string Title, List<IngredientDraft> Ingredients,
    List<StepDraft> Steps, List<string> Tags);
/// <summary>A saved recipe generated from a video.</summary>
public sealed record RecipeDocument(string Title, IReadOnlyList<Ingredient> Ingredients,
    IReadOnlyList<Step> Steps, IReadOnlyList<string> Uncertainties, IReadOnlyList<string> Tags,
    string Cover, IReadOnlyList<string> Frames, string Video, string? Attribution,
    string Model, decimal? Cost, string? SourceCaption, string? AudioModel, decimal? AudioCost,
    string? RecipeSource = null);

public static partial class RecipeEndpoints
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    public static void MapRecipes(this WebApplication app)
    {
        app.MapGet("/api/collection", async (string? search, string? tag, int? offset, IRecipeStore store, CancellationToken ct) =>
        {
            if (offset is < 0 || search?.Length > 200 || tag?.Length > 200)
            {
                return Results.Problem("Invalid search, tag or page offset.", statusCode: 400);
            }

            return Results.Ok(await store.Collection(search, offset ?? 0, 24, ct, tag));
        }).WithSummary("Browse a page of saved recipes");
        app.MapGet("/api/collection/tags", async (string? search, bool? popular, IRecipeStore store, CancellationToken ct) =>
        {
            if (search?.Length > 200)
            {
                return Results.Problem("Tag search is too long.", statusCode: 400);
            }

            return Results.Ok(await store.Tags(search, ct, popular ?? false));
        }).WithSummary("Find saved recipe tags").WithDescription("Returns up to 30 matching tags, optionally ordered by the number of recipes using each tag.");
        app.MapGet("/api/processing", async (bool? dismissed, int? offset, IRecipeStore store, ProcessingActivity activity, CancellationToken ct) =>
        {
            if (offset is < 0)
            {
                return Results.Problem("Invalid page offset.", statusCode: 400);
            }

            var page = await store.Processing(dismissed ?? false, offset ?? 0, 24, ct);
            return Results.Ok(page with
            {
                Items = page.Items.Select(activity.Attach).ToArray()
            });
        }).WithSummary("Browse processing jobs");
        app.MapPost("/api/recipes/{id:guid}/dismiss", async (Guid id, DismissRecipeRequest request, IRecipeStore store, CancellationToken ct) =>
            await store.Dismiss(id, request.Dismissed, ct) ? Results.NoContent() : Results.Conflict())
            .WithSummary("Hide or restore a failed job");
        app.MapGet("/api/recipes", async (string? search, IRecipeStore store, ProcessingActivity activity, CancellationToken ct) =>
            TypedResults.Ok((await store.List(search, ct)).Select(activity.Attach).ToArray())).WithSummary("Search recipes and processing jobs");
        app.MapGet("/api/recipes/{id:guid}", async (Guid id, IRecipeStore store, ProcessingActivity activity, CancellationToken ct) =>
            await store.Get(id, ct) is { } recipe ? Results.Ok(activity.Attach(recipe)) : Results.NotFound()).WithSummary("Get a recipe or processing job");
        app.MapPost("/api/recipes/{id:guid}/translation", async (Guid id, TranslateRecipeRequest request,
            IRecipeStore store, LocalTranslation translator, CancellationToken ct) =>
        {
            if (request.Language is not ("en" or "ru"))
            {
                return Results.Problem("Choose English or Russian.", statusCode: 400);
            }

            var recipe = await store.Get(id, ct);
            if (recipe?.Document is null || recipe.Status != "complete")
            {
                return Results.NotFound();
            }

            if (recipe.Version != request.Version)
            {
                return Results.Problem("The recipe changed. Reload it before translating.", statusCode: 409);
            }

            var document = JsonSerializer.Deserialize<RecipeDocument>(recipe.Document, _json);
            if (document is null)
            {
                return Results.Problem("The saved recipe could not be read.", statusCode: 500);
            }

            try
            {
                var translated = await translator.Translate(document, recipe.Version, request.Language, ct);
                var latest = await store.Get(id, ct);
                if (latest?.Status != "complete" || latest.Version != recipe.Version)
                {
                    return Results.Problem("The recipe changed during translation. Reload it before translating.", statusCode: 409);
                }

                return Results.Ok(translated);
            }
            catch (TranslationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 503);
            }
        }).WithSummary("Translate a saved recipe locally without changing it");
        app.MapPost("/api/recipes", async (CreateRecipeRequest request, IRecipeSubmissionService submissions, CancellationToken ct) =>
        {
            if (!request.Consent)
            {
                return Results.Problem("Consent is required before cloud analysis.", statusCode: 400);
            }

            var source = ReelSource.Canonicalize(request.Url);
            if (source is null)
            {
                return Results.Problem("Enter a public Instagram reel URL.", statusCode: 400);
            }

            RecipeResponse item;
            try
            {
                item = await submissions.QueueReel(source, request.Consent, request.AudioConsent, ct);
            }
            catch (DuplicateRecipeException)
            {
                return Results.Problem("This reel is already in your collection or processing list. Retry the existing failed job, or delete it before adding it again.", statusCode: 409);
            }
            return Results.Created($"/api/recipes/{item.Id}", item);
        }).WithSummary("Queue a public Instagram reel after consent");
        app.MapPost("/api/recipes/upload", async (HttpRequest request, IRecipeStore store, IRecipeSubmissionService submissions, AppPaths paths, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.Problem("Upload a video using a multipart form.", statusCode: 400);
            }

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("video");
            if (form["consent"] != "true")
            {
                return Results.Problem("Consent is required before cloud analysis.", statusCode: 400);
            }

            if (file is null || file.Length <= 0 || file.Length > 100_000_000)
            {
                return Results.Problem("Select a video smaller than 100 MB.", statusCode: 400);
            }

            var submittedUrl = form["url"].ToString();
            var source = string.IsNullOrWhiteSpace(submittedUrl) ? null : ReelSource.Canonicalize(submittedUrl);
            if (!string.IsNullOrWhiteSpace(submittedUrl) && source is null)
            {
                return Results.Problem("Enter a valid Instagram reel URL or leave it blank.", statusCode: 400);
            }

            await using var input = file.OpenReadStream();
            var hash = await MediaManagement.Hash(input, ct);
            RecipeResponse item;
            try
            {
                item = await submissions.StageUpload(source, hash, form["consent"] == "true", form["audioConsent"] == "true", ct);
            }
            catch (DuplicateRecipeException)
            {
                return Results.Problem("This reel or identical video is already in your collection or processing list. Delete the existing item before adding it again.", statusCode: 409);
            }
            try
            {
                var dir = paths.For(item.Id);
                Directory.CreateDirectory(dir);
                await using (var output = File.Create(Path.Combine(dir, "upload")))
                {
                    await file.CopyToAsync(output, ct);
                }

                await store.SetStatus(item.Id, "queued", null, ct);
            }
            catch
            {
                await store.SetStatus(item.Id, "failed", "Upload was interrupted. Upload the video again.", CancellationToken.None);
                throw;
            }
            return Results.Created($"/api/recipes/{item.Id}", item with
            {
                Status = "queued"
            });
        }).DisableAntiforgery().WithSummary("Queue an uploaded video after consent");
        app.MapDelete("/api/recipes/{id:guid}", async (Guid id, IRecipeStore store, AppPaths paths, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            if (!await store.BeginDelete(id, ct))
            {
                return await store.Get(id, ct) is null ? Results.NotFound() :
                    Results.Problem("Only failed jobs and completed recipes can be deleted. Wait for processing to finish.", statusCode: 409);
            }

            try
            {
                MediaManagement.DeleteRecipeMedia(paths, id);
                await store.FinishDelete(id, CancellationToken.None);
                return Results.NoContent();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NpgsqlException)
            {
                loggerFactory.CreateLogger("RecipeDeletion").LogError(ex, "Could not delete media for recipe {Id}", id);
                await store.SetStatus(id, "delete_failed", "Could not remove local media. Close any application using the files and try deleting again.", CancellationToken.None);
                return Results.Problem("Could not remove local media. The item was retained; try deleting it again.", statusCode: 500);
            }
        }).WithSummary("Permanently delete a failed job or saved recipe and its local media");
        app.MapPost("/api/recipes/{id:guid}/retry", async (Guid id, RetryRecipeRequest request, IRecipeStore store, CancellationToken ct) =>
        {
            if (!request.Consent)
            {
                return Results.Problem("Consent is required before cloud analysis.", statusCode: 400);
            }

            return await store.Retry(id, request.AudioConsent, ct) ? Results.Ok() : Results.Conflict();
        }).WithSummary("Retry a failed recipe without overwriting edits");
        app.MapPut("/api/recipes/{id:guid}", async (Guid id, EditRecipeRequest request, IRecipeStore store, CancellationToken ct) =>
        {
            var existing = await store.Get(id, ct);
            if (existing?.Document is null)
            {
                return Results.NotFound();
            }

            if (request.Tags is null || request.Tags.Count > 30 || request.Tags.Any(t => t is null || t.Length > 60))
            {
                return Results.Problem("Tags are invalid.", statusCode: 400);
            }

            var old = JsonSerializer.Deserialize<RecipeDocument>(existing.Document, _json)!;
            var validation = RecipeValidator.Validate(new(request.Title, request.Ingredients, request.Steps, []), double.MaxValue,
                allowCookingMeasures: true, sourceInstructions: old.RecipeSource is "caption" or "edited");
            if (validation.Recipe is null)
            {
                return Results.Problem(string.Join(" ", validation.Errors), statusCode: 400);
            }

            var updated = old with
            {
                Title = validation.Recipe.Title,
                Ingredients = validation.Recipe.Ingredients,
                Steps = validation.Recipe.Steps,
                Tags = request.Tags,
                RecipeSource = "edited"
            };
            if (!await store.Edit(id, request.Version, JsonSerializer.Serialize(updated, _json), ct))
            {
                return Results.Conflict();
            }

            return Results.Ok(await store.Get(id, ct));
        }).WithSummary("Correct a recipe using optimistic concurrency");
        app.MapGet("/api/media/{id:guid}/{name}", (Guid id, string name, AppPaths paths) =>
        {
            if (!MediaName().IsMatch(name))
            {
                return Results.NotFound();
            }

            var path = Path.Combine(paths.For(id), name);
            if (!File.Exists(path))
            {
                return Results.NotFound();
            }

            return Results.File(path, name.EndsWith(".jpg") ? "image/jpeg" : "video/mp4", enableRangeProcessing: true);
        }).WithSummary("View locally stored video or frame");
    }

    [GeneratedRegex(@"^(?:video\.mp4|frame\d{2}\.jpg)$", RegexOptions.CultureInvariant)]
    private static partial Regex MediaName();
}

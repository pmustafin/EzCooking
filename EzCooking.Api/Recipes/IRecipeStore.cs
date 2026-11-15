using EzCooking.Api.Data;

namespace EzCooking.Api.Recipes;

public interface IRecipeStore
{
    Task Initialize(CancellationToken ct);
    Task<RecipeResponse> Create(string? url, bool audioConsent, CancellationToken ct);
    Task<RecipeResponse> CreateUnique(string? url, string? uploadHash, bool audioConsent, CancellationToken ct);
    Task<RecipeResponse> QueueReel(string url, bool audioConsent, CancellationToken ct);
    Task<RecipeResponse> QueueReel(string url, bool audioConsent, EzCookingDbContext context, CancellationToken ct);
    Task<IReadOnlyList<Guid>> UploadsWithoutHash(CancellationToken ct);
    Task SetUploadHash(Guid id, string hash, CancellationToken ct);
    Task<bool> BeginDelete(Guid id, CancellationToken ct);
    Task FinishDelete(Guid id, CancellationToken ct);
    Task<RecipeResponse?> Get(Guid id, CancellationToken ct);
    Task<List<RecipeResponse>> List(string? search, CancellationToken ct);
    Task<RecipePage<RecipeSummary>> Collection(string? search, int offset, int limit, CancellationToken ct, string? tag = null);
    Task<IReadOnlyList<string>> Tags(string? search, CancellationToken ct, bool popular = false);
    Task<RecipePage<RecipeResponse>> Processing(bool dismissed, int offset, int limit, CancellationToken ct);
    Task<bool> Dismiss(Guid id, bool dismissed, CancellationToken ct);
    Task<RecipeResponse?> Claim(CancellationToken ct);
    Task SetStatus(Guid id, string status, string? error, CancellationToken ct);
    Task Complete(Guid id, string document, CancellationToken ct);
    Task<bool> Edit(Guid id, int version, string document, CancellationToken ct);
    Task<bool> Retry(Guid id, bool audioConsent, CancellationToken ct);
}

using EzCooking.Api.Data;

namespace EzCooking.Api.Recipes;

public interface IRecipeSubmissionService
{
    Task<RecipeResponse> QueueReel(string url, bool consent, bool audioConsent, CancellationToken ct);
    Task<RecipeResponse> QueueReel(string url, bool consent, bool audioConsent, EzCookingDbContext context, CancellationToken ct);
    Task<RecipeResponse> StageUpload(string? url, string hash, bool consent, bool audioConsent, CancellationToken ct);
}

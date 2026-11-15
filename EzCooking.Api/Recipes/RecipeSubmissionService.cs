using EzCooking.Api.Data;

namespace EzCooking.Api.Recipes;

public sealed class RecipeSubmissionService(IRecipeStore store) : IRecipeSubmissionService
{
    public Task<RecipeResponse> QueueReel(string url, bool consent, bool audioConsent, CancellationToken ct)
    {
        EnsureConsent(consent);
        return store.QueueReel(RequireReel(url), audioConsent, ct);
    }

    public Task<RecipeResponse> QueueReel(string url, bool consent, bool audioConsent, EzCookingDbContext context, CancellationToken ct)
    {
        EnsureConsent(consent);
        return store.QueueReel(RequireReel(url), audioConsent, context, ct);
    }

    public Task<RecipeResponse> StageUpload(string? url, string hash, bool consent, bool audioConsent, CancellationToken ct)
    {
        EnsureConsent(consent);
        return store.CreateUnique(url is null ? null : RequireReel(url), hash, audioConsent, ct);
    }

    private static string RequireReel(string url) =>
        ReelSource.Canonicalize(url) ?? throw new ArgumentException("Enter a public Instagram reel URL.", nameof(url));

    private static void EnsureConsent(bool consent)
    {
        if (!consent)
        {
            throw new InvalidOperationException("Consent is required before cloud analysis.");
        }
    }
}

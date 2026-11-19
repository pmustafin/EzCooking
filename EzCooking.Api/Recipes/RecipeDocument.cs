using EzCooking.RecipeValidation;

namespace EzCooking.Api.Recipes;

/// <summary>A saved recipe generated from a video.</summary>
public sealed record RecipeDocument(string Title, IReadOnlyList<Ingredient> Ingredients,
    IReadOnlyList<Step> Steps, IReadOnlyList<string> Uncertainties, IReadOnlyList<string> Tags,
    string Cover, IReadOnlyList<string> Frames, string Video, string? Attribution,
    string Model, decimal? Cost, string? SourceCaption, string? AudioModel, decimal? AudioCost,
    string? RecipeSource = null);

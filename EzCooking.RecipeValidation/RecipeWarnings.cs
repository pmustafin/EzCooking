namespace EzCooking.RecipeValidation;

public static class RecipeWarnings
{
    // Warnings mean "check this before trusting the recipe" and mark it for review.
    // Notes are informational: they tell the reader where an amount came from.
    public static IReadOnlyList<string> CaptionAmountWarnings(string? caption, string? recipeSource, IReadOnlyList<Ingredient> ingredients)
    {
        var warnings = new List<string>();
        if (caption is null || recipeSource != "caption" || !CaptionInstructions.HasIngredientQuantities(caption))
        {
            return warnings;
        }

        if (ingredients.All(ingredient => ingredient.EstimatedAmount is null))
        {
            warnings.Add("The caption contains quantities that were not extracted; check the original.");
        }
        else if (ingredients.Any(ingredient => ingredient.EstimatedAmount is not null &&
            (!RecipeValidator.IsUsableAmount(ingredient.EstimatedAmount) ||
             CaptionInstructions.AssociatedAmount(caption, ingredient.Name, ingredient.EstimatedAmount) is null)))
        {
            warnings.Add("Some ingredient amounts could not be matched reliably to the caption; check the original.");
        }

        return warnings;
    }

    public static string? CaptionAmountNote(string? caption, string? recipeSource)
    {
        if (caption is null || recipeSource is "caption" or "edited" || !CaptionInstructions.HasIngredientQuantities(caption))
        {
            return null;
        }

        return "The public caption lists quantities. The amounts here are AI estimates, not the creator's; check the caption before cooking.";
    }
}

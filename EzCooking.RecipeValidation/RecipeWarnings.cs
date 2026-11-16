namespace EzCooking.RecipeValidation;

public static class RecipeWarnings
{
    // These warnings describe what the source states and what the recipe did with it.
    // They do not verify the recipe; they tell the reader where to double-check.
    public static IReadOnlyList<string> CaptionAmountWarnings(string? caption, string? recipeSource, IReadOnlyList<Ingredient> ingredients)
    {
        var warnings = new List<string>();
        if (caption is null || !CaptionInstructions.HasIngredientQuantities(caption))
        {
            return warnings;
        }

        if (recipeSource == "caption")
        {
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
        }
        else if (recipeSource is not "edited")
        {
            warnings.Add("The public caption lists quantities. The amounts here are AI estimates, not the creator's; check the caption before cooking.");
        }

        return warnings;
    }
}

namespace EzCooking.RecipeValidation;

public static class AmountSource
{
    public const string Caption = "caption";
    public const string Estimated = "estimated";

    // Labels where each amount came from. A caption method supplies the amounts directly;
    // otherwise an amount counts as source-grounded only when it sits next to the ingredient
    // in the caption, so the label never claims more than the caption supports.
    public static IReadOnlyList<Ingredient> Label(string? caption, bool sourceInstructions, IReadOnlyList<Ingredient> ingredients)
    {
        var labelled = new List<Ingredient>(ingredients.Count);
        foreach (var ingredient in ingredients)
        {
            var source = SourceOf(caption, sourceInstructions, ingredient);
            labelled.Add(source is null ? ingredient : ingredient with
            {
                AmountSource = source
            });
        }

        return labelled;
    }

    private static string? SourceOf(string? caption, bool sourceInstructions, Ingredient ingredient)
    {
        if (string.IsNullOrWhiteSpace(ingredient.EstimatedAmount))
        {
            return null;
        }

        if (sourceInstructions)
        {
            return Caption;
        }

        if (caption is null)
        {
            return Estimated;
        }

        return CaptionInstructions.AssociatedAmount(caption, ingredient.Name, ingredient.EstimatedAmount) is not null
            ? Caption
            : Estimated;
    }
}

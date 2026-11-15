using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace EzCooking.RecipeValidation;

public sealed record IngredientDraft(string? Name, string? Amount, string? Evidence);
public sealed record StepDraft(string? Text,
    [property: JsonConverter(typeof(TolerantTimestampConverter))] double? TimestampSeconds, string? Evidence);
public sealed record RecipeDraft(
    string? Title,
    IReadOnlyList<IngredientDraft?>? Ingredients,
    IReadOnlyList<StepDraft?>? Steps,
    IReadOnlyList<string?>? Uncertainties);

public sealed record Ingredient(string Name, string? EstimatedAmount);
public sealed record Step(string Text, double? TimestampSeconds);
public sealed record ValidatedRecipe(
    string Title,
    IReadOnlyList<Ingredient> Ingredients,
    IReadOnlyList<Step> Steps,
    IReadOnlyList<string> Uncertainties);
public sealed record ValidationResult(ValidatedRecipe? Recipe, IReadOnlyList<string> Errors);

public static partial class RecipeValidator
{
    private static readonly HashSet<string> _descriptiveTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "baked", "creamy", "crispy", "easy", "fresh", "homemade", "quick", "roasted", "spicy",
        "vegan", "vegetarian", "italian", "russian", "french", "chinese", "mexican", "asian", "sicilian"
    };
    // These checks do not establish factual accuracy or food safety.
    private const string DefaultUncertainty =
        "Ingredients, quantities, and instructions may be inferred or estimated; verify them against the source.";

    public static ValidationResult Validate(RecipeDraft? draft, double videoDurationSeconds,
        bool allowCookingMeasures = false, bool allowTimestamps = true, bool sourceInstructions = false)
    {
        var errors = new List<string>();
        if (draft is null)
        {
            return new(null, ["No recipe was returned."]);
        }

        if (!double.IsFinite(videoDurationSeconds) || videoDurationSeconds <= 0)
        {
            return new(null, ["The video duration is unavailable or invalid."]);
        }

        var title = draft.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200 || !LetterRegex().IsMatch(title))
        {
            errors.Add("The recipe needs a meaningful title.");
        }

        var ingredients = new List<Ingredient>();
        if (draft.Ingredients is null || draft.Ingredients.Count is 0 or > 100)
        {
            errors.Add("The recipe needs between 1 and 100 ingredients.");
        }
        else
        {
            foreach (var item in draft.Ingredients)
            {
                var name = item?.Name?.Trim();
                var amount = item?.Amount?.Trim();
                if (string.IsNullOrWhiteSpace(name) || name.Length > 150 || !LetterRegex().IsMatch(name))
                {
                    errors.Add("An ingredient has no usable name.");
                    continue;
                }
                if (!string.IsNullOrEmpty(amount) && !IsUsableAmount(amount))
                {
                    errors.Add($"The amount for {name} is not usable.");
                    continue;
                }
                ingredients.Add(new(name, string.IsNullOrEmpty(amount) ? null : amount));
            }
        }

        var steps = new List<Step>();
        if (draft.Steps is null || draft.Steps.Count is 0 or > 100)
        {
            errors.Add("The recipe needs between 1 and 100 cooking steps.");
        }
        else
        {
            foreach (var item in draft.Steps)
            {
                var text = item?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(text) || text.Length is < 12 or > 2000 ||
                    (!sourceInstructions && !ActionRegex().IsMatch(text)) || !MultipleWordsRegex().IsMatch(text))
                {
                    errors.Add("A step does not contain a recognizable cooking action.");
                    continue;
                }
                if (!allowCookingMeasures && CookingMeasureRegex().IsMatch(text))
                {
                    errors.Add("A cooking time or temperature cannot be verified against the source.");
                    continue;
                }
                var timestamp = item?.TimestampSeconds;
                if (!allowTimestamps || (timestamp is not null &&
                    (!double.IsFinite(timestamp.Value) || timestamp < 0 || timestamp >= videoDurationSeconds)))
                {
                    timestamp = null;
                }

                steps.Add(new(text, timestamp));
            }
        }

        if (steps.Count != 0)
        {
            foreach (var ingredient in ingredients)
            {
                var terms = IngredientTermRegex().Matches(ingredient.Name)
                    .Select(m => m.Value)
                    .Where(term => term.Length >= 3)
                    .ToArray();
                if (terms.Length == 0 || !terms.Any(term => steps.Any(step =>
                    Regex.IsMatch(step.Text, $@"(?<!\p{{L}}){Regex.Escape(term)}\p{{L}}*(?!\p{{L}})",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))))
                {
                    errors.Add($"The instructions do not explain how to use {ingredient.Name}. Mention it explicitly in the relevant cooking step or remove it if unsupported.");
                }
            }
        }

        if (errors.Count != 0)
        {
            return new(null, errors);
        }

        var uncertainties = (draft.Uncertainties ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim())
            .ToList();
        uncertainties.Insert(0, sourceInstructions
            ? "Instructions are from the public caption. Ingredient extraction and quantities may be incomplete or misinterpreted; verify against the original."
            : DefaultUncertainty);
        if (ingredients.Any(i => i.EstimatedAmount is null))
        {
            uncertainties.Add("Some ingredient quantities are unknown.");
        }

        return new(new(title!, ingredients, steps, uncertainties), []);
    }

    public static bool HasCookingAction(string text) => ActionRegex().IsMatch(text);
    public static bool IsUsableAmount(string amount) => amount.Length <= 100 && AmountRegex().IsMatch(amount) &&
        !NegativeAmountRegex().IsMatch(amount) && !ZeroAmountRegex().IsMatch(amount);

    public static IReadOnlyList<string> FindTitleTagConflicts(string? title,
        IEnumerable<string?>? ingredientNames, IEnumerable<string?>? tags)
    {
        var titleTerms = IngredientTermRegex().Matches(title ?? "")
            .Select(m => m.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ingredientTerms = (ingredientNames ?? [])
            .SelectMany(name => IngredientTermRegex().Matches(name ?? "").Select(m => m.Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // shortcut: descriptive title tags may need review; use source-grounded entity extraction when available.
        return (tags ?? []).Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag!.Trim())
            .Where(tag => SingleTermRegex().IsMatch(tag) && tag.Length >= 3 &&
                titleTerms.Contains(tag) && !_descriptiveTags.Contains(tag) && !ingredientTerms.Contains(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(tag => $"The title and tags mention {tag}, which is not listed as an ingredient. If it is an ingredient, check the original.")
            .ToArray();
    }

    [GeneratedRegex(@"\p{L}")]
    private static partial Regex LetterRegex();

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex IngredientTermRegex();

    [GeneratedRegex(@"^\p{L}+$")]
    private static partial Regex SingleTermRegex();

    [GeneratedRegex(@"^(?=.*(?:\d|½|¼|¾|⅓|⅔|to taste|по вкусу))(?:[\p{L}\p{N}\p{M}\s.,/½¼¾⅓⅔+\-–]|\([\p{L}\p{N}\p{M}\s.,/½¼¾⅓⅔+\-–]+\))+$", RegexOptions.IgnoreCase)]
    private static partial Regex AmountRegex();

    [GeneratedRegex(@"(?:^|\s|\()-\s*\d")]
    private static partial Regex NegativeAmountRegex();

    [GeneratedRegex(@"(?:^|\()\s*0+(?:[.,]0+)?(?:\s|[-–)]|$)|/\s*0+(?:\s|\)|$)")]
    private static partial Regex ZeroAmountRegex();

    [GeneratedRegex(@"\p{L}{2,}\s+\p{L}{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex MultipleWordsRegex();

    [GeneratedRegex(@"(?<!\d)\d+(?:[.,]\d+)?\s*(?:°\s*[cfсф]|degrees?\b|градус\p{L}*|minutes?\b|mins?\b|hours?\b|hrs?\b|seconds?\b|минут\p{L}*|секунд\p{L}*|час(?:а|ов)?\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CookingMeasureRegex();

    [GeneratedRegex(@"\b(?:add|bake|baste|beat|blanch|boil|brown|brush|chop|combine|cook|cover|cut|deglaze|dice|drain|fold|fry|garnish|grate|grill|heat|knead|marinate|melt|mince|mix|peel|place|plate|pour|preheat|reduce|remove|rinse|roast|roll|saut[eé]|sear|season|serve|shred|simmer|slice|spoon|sprinkle|squeeze|stir|strain|toast|top|toss|transfer|warm|whisk|разогре\p{L}*|добав\p{L}*|вар\p{L}*|обжар\p{L}*|жар\p{L}*|нареж\p{L}*|реж\p{L}*|смеш\p{L}*|перемеш\p{L}*|готов\p{L}*|запек\p{L}*|пода\p{L}*|посол\p{L}*|посып\p{L}*|вылож\p{L}*|налей\p{L}*|слей\p{L}*|туш\p{L}*|очист\p{L}*|измельч\p{L}*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ActionRegex();
}

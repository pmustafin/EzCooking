using System.Text.RegularExpressions;

namespace EzCooking.RecipeValidation;

public static partial class CaptionInstructions
{
    public static IReadOnlyList<string>? Find(string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
        {
            return null;
        }

        var steps = new List<string>();
        var lines = caption.Split('\n');
        var append = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var match = NumberedLine().Match(trimmed);
            if (!match.Success)
            {
                if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed is "-" or "—")
                {
                    append = false;
                }
                else if (append)
                {
                    steps[^1] += " " + trimmed;
                }

                continue;
            }
            if (!int.TryParse(match.Groups[1].Value, out var number) || number != steps.Count + 1)
            {
                return null;
            }

            var text = match.Groups[2].Value.Trim();
            steps.Add(text);
            append = true;
        }
        if (steps.Count == 0)
        {
            var method = Array.FindIndex(lines, line => MethodHeading().IsMatch(line.Trim()));
            if (method >= 0)
            {
                foreach (var line in lines.Skip(method + 1))
                {
                    var text = line.Trim().TrimStart('•', '-', '–', ' ').Trim();
                    if (text.Length == 0 && steps.Count == 0)
                    {
                        continue;
                    }

                    if (text.Length == 0 || text.StartsWith('#'))
                    {
                        break;
                    }

                    steps.Add(text);
                }
            }
        }
        // shortcut: ambiguous or unstructured captions need richer extraction with source verification.
        return steps.Count >= 2 && steps.All(s => s.Length is >= 12 and <= 2000) &&
            steps.Count(RecipeValidator.HasCookingAction) >= 2 ? steps : null;
    }

    public static bool IngredientNamesAreGrounded(string caption, IEnumerable<IngredientDraft?>? ingredients) =>
        ingredients is not null && ingredients.All(i =>
            !string.IsNullOrWhiteSpace(i?.Name) &&
            ContainsText(caption, i.Name.Trim()));

    public static bool HasIngredientQuantities(string caption) => Quantity().IsMatch(caption);

    public static string? AssociatedAmount(string caption, string name, string amount)
    {
        var separated = Regex.Replace(amount.Trim(), @"(?<=\d)(?=\p{L})", " ");
        var amountPattern = string.Join("[ \t]*", Regex.Split(separated, @"[ \t]+").Select(Regex.Escape));
        var quantity = $@"(?<![\p{{L}}\p{{N}}:/.])(?<quantity>{amountPattern})(?![\p{{L}}\p{{N}}:/.])";
        var ingredient = Regex.Escape(name.Trim());
        var pattern = $@"(?:{quantity}(?:[ \t]+\p{{L}}+){{0,3}}[ \t]+{ingredient}|{ingredient}[ \t]*[-–]?[ \t]*{quantity})";
        var match = Regex.Match(caption, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
        return match.Success ? match.Groups["quantity"].Value : null;
    }

    private static bool ContainsText(string caption, string value) =>
        Regex.IsMatch(caption, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(value)}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    [GeneratedRegex(@"^(\d{1,2})[.)]\s+(.+)$")]
    private static partial Regex NumberedLine();
    [GeneratedRegex(@"^(?:method|directions|instructions|preparation|способ приготовления|приготовление|инструкция)\s*:?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MethodHeading();
    [GeneratedRegex(@"(?<![\p{L}\p{N}])\d+(?:[.,/]\d+)?\s*(?:kg|g|ml|l|tbsp|tsp|cups?|tablespoons?|teaspoons?|grams?|килограмм\p{L}*|грамм\p{L}*|мл|столовы\p{L}*|чайны\p{L}*)(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Quantity();
}

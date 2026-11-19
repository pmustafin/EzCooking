using System.Text.RegularExpressions;
using EzCooking.Api.Recipes;

namespace EzCooking.Api.Translation;

/// <summary>Translation payload shape and its number-preserving check.</summary>
public static partial class TranslationText
{
    public static string[] Texts(RecipeDocument document) =>
        [document.Title, .. document.Ingredients.SelectMany(i => i.EstimatedAmount is null ? [i.Name] : new[] { i.Name, i.EstimatedAmount }),
         .. document.Steps.Select(s => s.Text), .. document.Uncertainties, .. document.Tags];

    public static bool Valid(IReadOnlyList<string> original, IReadOnlyList<string?>? translated) =>
        translated is not null && original.Count == translated.Count &&
        original.Zip(translated).All(pair =>
            !string.IsNullOrWhiteSpace(pair.Second) && pair.Second.Length <= 4000 &&
            Numbers().Matches(pair.First).Select(m => m.Value)
                .Order(StringComparer.Ordinal)
                .SequenceEqual(Numbers().Matches(pair.Second).Select(m => m.Value).Order(StringComparer.Ordinal)));

    [GeneratedRegex(@"[-+−]?[\d]+(?:[.,:/-][\d]+)*|[¼½¾⅓⅔⅛⅜⅝⅞]", RegexOptions.CultureInvariant)]
    private static partial Regex Numbers();
}

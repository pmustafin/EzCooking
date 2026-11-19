using EzCooking.RecipeValidation;
using System.Text.Json;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var draft = new RecipeDraft(
    "Pasta",
    [new("pasta", "200 g", "visible"), new("butter", null, "visible")],
    [new("Cook the pasta in boiling water.", 3, "visible"),
     new("Toss the pasta with butter.", 999, "visible")],
    []);

var result = RecipeValidator.Validate(draft, 30);
Check(result.Recipe is not null && result.Errors.Count == 0, "Valid draft was rejected.");
Check(result.Recipe!.Ingredients[0].EstimatedAmount == "200 g", "Amount should remain an estimate.");
Check(result.Recipe.Steps[0].TimestampSeconds == 3, "Valid timestamp was lost.");
Check(result.Recipe.Steps[1].TimestampSeconds is null, "Out-of-range timestamp was not omitted.");
Check(RecipeValidator.Validate(draft, 30, allowTimestamps: false).Recipe!.Steps[0].TimestampSeconds is null,
    "Unverified model timestamps must be omitted.");
Check(result.Recipe.Uncertainties.Count >= 2, "Uncertainty must not be lost.");

var badSteps = draft with { Steps = [new("123 456", 1, "visible")] };
Check(RecipeValidator.Validate(badSteps, 30).Recipe is null, "Numbers are not cooking instructions.");
var badAmount = draft with { Ingredients = [new("pasta", "many", "visible")] };
Check(RecipeValidator.Validate(badAmount, 30).Recipe is null, "Unusable amounts must be rejected.");
var negativeAmount = draft with { Ingredients = [new("pasta", "-2 g", "visible")] };
Check(RecipeValidator.Validate(negativeAmount, 30).Recipe is null, "Negative amounts must be rejected.");
var zeroAmount = draft with { Ingredients = [new("pasta", "0 g", "visible")] };
Check(RecipeValidator.Validate(zeroAmount, 30).Recipe is null, "Zero amounts must be rejected.");
var fractionalAmount = draft with { Ingredients = [new("pasta", "0.5 g", "visible")] };
Check(RecipeValidator.Validate(fractionalAmount, 30).Recipe is not null, "Positive fractional amounts must remain valid.");
var parenthesizedAmount = draft with
{
    Ingredients = [new("pasta", "2 pieces (about 250–300 g each)", "inferred")],
    Steps = [new("Spoon the pasta onto a plate.", null, "inferred")]
};
var parenthesizedResult = RecipeValidator.Validate(parenthesizedAmount, 30);
Check(parenthesizedResult.Recipe is not null &&
      parenthesizedResult.Recipe.Ingredients[0].EstimatedAmount == "2 pieces (about 250–300 g each)",
    "A valid parenthesized quantity and spooning action must be accepted.");
Check(!RecipeValidator.IsUsableAmount("2 pieces (-3 g)") && !RecipeValidator.IsUsableAmount("2 pieces (0 g)") &&
      !RecipeValidator.IsUsableAmount("2 pieces (3 g") && !RecipeValidator.IsUsableAmount("2 pieces )3 g("),
    "Parenthesized amounts must not allow negative, zero or malformed quantities.");
Check(RecipeValidator.Validate(draft, double.NaN).Recipe is null, "Invalid duration must be rejected.");
Check(RecipeValidator.Validate(null, 30).Recipe is null, "Missing output must be rejected.");
var russian = draft with { Ingredients = [new("пасту", null, "inferred")], Steps = [new("Добавьте пасту в горячую воду.", -1, "visible")] };
var russianResult = RecipeValidator.Validate(russian, 30);
Check(russianResult.Recipe is not null, "Russian cooking instruction was rejected.");
Check(russianResult.Recipe!.Steps[0].TimestampSeconds is null, "Negative timestamp was not omitted.");
var endTimestamp = draft with { Ingredients = [new("pasta", null, "inferred")], Steps = [new("Cook the pasta in boiling water.", 30, "visible")] };
Check(RecipeValidator.Validate(endTimestamp, 30).Recipe!.Steps[0].TimestampSeconds is null,
    "Timestamp at the duration boundary must be omitted.");
var malformedTimestamp = JsonSerializer.Deserialize<StepDraft>("""{"Text":"Cook the pasta in boiling water.","TimestampSeconds":"not a timestamp"}""");
Check(malformedTimestamp?.TimestampSeconds is null, "Malformed model timestamps must be omitted.");
var observedActions = draft with
{
    Ingredients = [new("butter", null, "inferred"), new("pasta", null, "inferred")],
    Steps = [
    new("Melt butter in a pan over heat", null, "inferred"),
    new("Plate the pasta onto a serving dish", null, "inferred"),
    new("Garnish the dish with butter", null, "inferred")
]
};
Check(RecipeValidator.Validate(observedActions, 30).Recipe is not null, "Common cooking actions were rejected.");
var burritoBowl = new RecipeDraft("Burrito bowl",
    [new("avocado", "2", null), new("Monterey Jack cheese", "1/2 cup", null),
     new("sour cream", "2 tbsp", null), new("romaine lettuce", "2 cups", null),
     new("pico de gallo", "1/2 cup", null)],
    [new("Mash avocados to make guacamole.", null, null),
     new("Arrange guacamole, lettuce, Monterey Jack cheese, sour cream, and pico de gallo in a bowl.", null, null),
     new("Portion the finished bowls into containers.", null, null)], []);
Check(RecipeValidator.Validate(burritoBowl, 30).Recipe is not null,
    "Valid preparation, assembly and portioning steps must not cause missing ingredient errors.");
var unrecognizedStep = burritoBowl with
{
    Steps = [new("Mash avocados to make guacamole.", null, null),
        new("The cheese, lettuce, sour cream, and pico de gallo.", null, null)]
};
var unrecognizedResult = RecipeValidator.Validate(unrecognizedStep, 30);
Check(unrecognizedResult.Recipe is null && unrecognizedResult.Errors.Count == 1 &&
      unrecognizedResult.Errors[0].Contains("recognizable cooking action"),
    "A rejected step must not also cause misleading missing ingredient errors.");
var sauce = new RecipeDraft("Sauce",
    [new("mayonnaise", "1/2 cup", null), new("black pepper", null, null),
     new("honey", "1 tbsp", null), new("garlic powder", "1 tsp", null)],
    [new("Scoop mayonnaise into a bowl.", null, null),
     new("Grind black pepper into the bowl.", null, null),
     new("Whisk the sauce until smooth.", null, null)], []);
var incompleteSauce = RecipeValidator.Validate(sauce, 30);
Check(incompleteSauce.Recipe is null && incompleteSauce.Errors.Count == 2 &&
      incompleteSauce.Errors.Any(error => error.Contains("honey")) &&
      incompleteSauce.Errors.Any(error => error.Contains("garlic powder")),
    "Scoop and grind are valid actions, but genuinely unused ingredients must still be reported.");
var completeSauce = sauce with
{
    Steps = [.. sauce.Steps!, new("Add honey and garlic powder to the sauce.", null, null)]
};
Check(RecipeValidator.Validate(completeSauce, 30).Recipe is not null,
    "A method using all sauce ingredients must pass.");
var rejectedAndMissing = sauce with
{
    Steps = [new("The mayonnaise is in a bowl.", null, null)]
};
var rejectedAndMissingResult = RecipeValidator.Validate(rejectedAndMissing, 30);
Check(rejectedAndMissingResult.Errors.Any(error => error.Contains("recognizable cooking action")) &&
      rejectedAndMissingResult.Errors.Any(error => error.Contains("honey")) &&
      !rejectedAndMissingResult.Errors.Any(error => error.Contains("use mayonnaise")),
    "Feedback must report genuinely missing ingredients without blaming an ingredient in a rejected step.");
var missingIngredient = draft with { Ingredients = [new("lobster", null, "inferred")] };
Check(RecipeValidator.Validate(missingIngredient, 30).Recipe is null, "Ingredients missing from the method must be rejected.");
var steak = new RecipeDraft("Steak", [new("steak", "2 pieces (about 250–300 g total)", null),
    new("peppercorns", "1 tsp", null)],
    [new("Sear the steak in a hot pan.", null, null), new("Spoon pepper sauce over the steak.", null, null)], []);
var incompleteSteak = RecipeValidator.Validate(steak, 30);
Check(incompleteSteak.Recipe is null && incompleteSteak.Errors.Count == 1 &&
      incompleteSteak.Errors[0].Contains("peppercorns"),
    "Searing, spooning and the amount should be accepted, while unexplained peppercorns remain an error.");
var completeSteak = steak with
{
    Steps = [new("Sear the steak in a hot pan.", null, null),
    new("Add peppercorns to the sauce and spoon it over the steak.", null, null)]
};
Check(RecipeValidator.Validate(completeSteak, 30).Recipe is not null,
    "A method explicitly using peppercorns should pass.");
var unverifiedTime = draft with { Ingredients = [new("pasta", null, "inferred")], Steps = [new("Cook pasta for 10 minutes.", null, "visible")] };
Check(RecipeValidator.Validate(unverifiedTime, 30).Recipe is null, "Unverified cooking times must be rejected.");
Check(RecipeValidator.Validate(unverifiedTime, 30, allowCookingMeasures: true).Recipe is not null,
    "User corrections may include cooking times.");
var unverifiedTemperature = draft with { Ingredients = [new("pasta", null, "inferred")], Steps = [new("Bake pasta at 180°C.", null, "visible")] };
Check(RecipeValidator.Validate(unverifiedTemperature, 30).Recipe is null, "Unverified temperatures must be rejected.");
var reviewDraft = new RecipeDraft("Garlic pasta",
    [new("pasta", "200 g", null), new("garlic", "1 clove", null)],
    [new("Enrobe the pasta in sauce.", null, null)], []);
var reviewResult = RecipeValidator.Validate(reviewDraft, 30, allowReview: true, sanitizeModelValues: true);
Check(reviewResult.Recipe is not null && reviewResult.Errors.Count == 0 &&
      reviewResult.Warnings.Any(w => w.Contains("cooking action")) &&
      reviewResult.Warnings.Any(w => w.Contains("garlic")),
    "Unrecognized actions and unmatched ingredients should produce a reviewable draft.");
Check(RecipeValidator.Validate(badSteps, 30, allowReview: true, sanitizeModelValues: true).Recipe is null &&
      RecipeValidator.Validate(draft with { Ingredients = [] }, 30, allowReview: true, sanitizeModelValues: true).Recipe is null,
    "Numbers-only instructions and empty ingredients must still fail.");
var invalidModelAmount = RecipeValidator.Validate(negativeAmount, 30, allowReview: true, sanitizeModelValues: true);
Check(invalidModelAmount.Recipe?.Ingredients[0].EstimatedAmount is null &&
      invalidModelAmount.Warnings.Any(w => w.Contains("quantities are unknown")),
    "An invalid model amount must be omitted and marked for review.");
Check(RecipeValidator.Validate(negativeAmount, 30, allowReview: true).Recipe is null,
    "Invalid user-entered amounts must not be silently changed.");
var unsafeMeasures = draft with
{
    Ingredients = [new("pasta", "200 g", null)],
    Steps = [new("Bake pasta at 180°C for 10-15 minutes.", 12, null)]
};
var safeReview = RecipeValidator.Validate(unsafeMeasures, 30, allowTimestamps: false,
    allowReview: true, sanitizeModelValues: true);
Check(safeReview.Recipe is not null && !safeReview.Recipe.Steps[0].Text.Any(char.IsDigit) &&
      safeReview.Recipe.Steps[0].TimestampSeconds is null &&
      safeReview.Warnings.Any(w => w.Contains("unverified cooking time")),
    "Unverified temperature and time ranges must be omitted and flagged.");
var savedSafeDraft = new RecipeDraft(safeReview.Recipe!.Title,
    safeReview.Recipe.Ingredients.Select(i => new IngredientDraft(i.Name, i.EstimatedAmount, null)).ToArray(),
    safeReview.Recipe.Steps.Select(s => new StepDraft(s.Text, s.TimestampSeconds, null)).ToArray(), []);
Check(RecipeValidator.Validate(savedSafeDraft, 30, allowCookingMeasures: true, allowReview: true)
      .Warnings.Any(w => w.Contains("unverified cooking time")),
    "The warning for omitted measures must survive saving and reloading.");
Check(RecipeValidator.Validate(unsafeMeasures, 30, allowCookingMeasures: true, allowReview: true)
      .Recipe!.Steps[0].Text.Contains("180°C"),
    "Explicit user corrections may retain cooking measures.");
var abbreviatedTime = unsafeMeasures with { Steps = [new("Cook pasta for 1 1/2 h and 30 sec.", null, null)] };
Check(!RecipeValidator.Validate(abbreviatedTime, 30, allowReview: true, sanitizeModelValues: true)
      .Recipe!.Steps[0].Text.Any(char.IsDigit),
    "Abbreviated unverified cooking times must also be omitted.");
Check(RecipeValidator.FindTitleTagConflicts("Spicy Chicken Pasta", ["pasta"], ["spicy", "chicken", "pasta"]).Count == 1,
    "A title ingredient repeated in tags must not disappear from the ingredient list.");
Check(RecipeValidator.FindTitleTagConflicts("Spicy Chicken Pasta", ["chicken", "pasta"], ["spicy", "chicken", "pasta"]).Count == 0,
    "Descriptive tags should not be required as ingredients.");
Check(RecipeValidator.FindTitleTagConflicts("Sicilian Pasta", ["pasta"], ["Sicilian", "pasta"]).Count == 0,
    "Sicilian describes a regional style, not an ingredient.");
var ambiguousTitleWarning = RecipeValidator.FindTitleTagConflicts("Japanese Chicken Pasta", ["pasta"],
    ["Japanese", "chicken", "pasta"]);
Check(ambiguousTitleWarning.Count == 2 && ambiguousTitleWarning.All(w => w.Contains("If it is an ingredient")),
    "Ambiguous tags must be framed as review warnings rather than confirmed missing ingredients.");

var caption = "A simple meal\n1. Boil pasta for 10 minutes.\n2. Add 2 tbsp butter and stir the pasta.\n#dinner";
var captionSteps = CaptionInstructions.Find(caption);
Check(captionSteps is { Count: 2 } && captionSteps[0] == "Boil pasta for 10 minutes.",
    "Caption instructions must be preserved in order.");
Check(CaptionInstructions.Find("1. Visit our shop\n2. Share this video") is null,
    "A numbered promotion is not a recipe.");
Check(CaptionInstructions.Find("1. Boil the pasta.\n3. Add butter and stir.") is null,
    "Nonconsecutive steps should not be accepted as a full method.");
Check(CaptionInstructions.Find("Method:\n- Boil the pasta in water.\n- Stir the pasta with butter.\n\n#food") is { Count: 2 },
    "A method heading with unnumbered steps should be recognized.");
Check(CaptionInstructions.Find("1. Boil the pasta\nuntil cooked.\n2. Add butter and stir.")?[0] ==
    "Boil the pasta until cooked.", "Wrapped instructions must not be discarded.");
Check(CaptionInstructions.IngredientNamesAreGrounded(caption, [new("butter", "2 tbsp", null)]),
    "Caption ingredient should be grounded.");
Check(CaptionInstructions.HasIngredientQuantities(caption) &&
      !CaptionInstructions.HasIngredientQuantities("Bake for 10 minutes at 180 degrees."),
    "Ingredient quantities should be distinguished from cooking times.");
Check(CaptionInstructions.AssociatedAmount(caption, "butter", "4 tbsp") is null &&
      !CaptionInstructions.IngredientNamesAreGrounded(caption, [new("cheese", null, null)]) &&
      CaptionInstructions.AssociatedAmount("Add 12 tbsp butter.", "butter", "2 tbsp") is null,
    "Invented caption ingredients and amounts must be rejected.");
Check(CaptionInstructions.AssociatedAmount("Add 350g soft unsalted butter.", "soft unsalted butter", "350g") == "350g" &&
      CaptionInstructions.AssociatedAmount("Add 2tbsp butter.", "butter", "2 tbsp") == "2tbsp",
    "An adjacent source amount should be associated with its ingredient.");
Check(CaptionInstructions.AssociatedAmount("1. Season with a 3:1 salt to sugar ratio.", "salt", "3") is null &&
      CaptionInstructions.AssociatedAmount("1. Use a fresh turkey.", "turkey", "1") is null &&
      CaptionInstructions.AssociatedAmount("Add 3\npasta.", "pasta", "3") is null &&
      CaptionInstructions.AssociatedAmount("Add 3 cloves garlic, then sea salt.", "sea salt", "3") is null,
    "Ratios, step numbers and other ingredients' quantities must not become source amounts.");
Check(!RecipeValidator.IsUsableAmount("3:1"), "A ratio cannot be presented as one ingredient's amount.");
var sourceDraft = new RecipeDraft("Pasta", [new("pasta", null, null), new("butter", "2 tbsp", null)],
    captionSteps!.Select(s => (StepDraft?)new(s, null, null)).ToArray(), []);
var sourceResult = RecipeValidator.Validate(sourceDraft, 30, allowCookingMeasures: true, sourceInstructions: true);
Check(sourceResult.Recipe is not null && sourceResult.Recipe.Steps[0].Text == captionSteps![0],
    "Caption steps including source times must be retained.");
Check(RecipeValidator.Validate(sourceDraft, 30).Recipe is null, "Video-generated times remain disallowed.");

var quantifiedCaption = "Beef stroganoff\n300 g beef\n2 tbsp butter";
var groundedIngredients = new[] { new Ingredient("beef", "300 g"), new Ingredient("butter", "2 tbsp") };
var videoNote = RecipeWarnings.CaptionAmountNote(quantifiedCaption, "video");
Check(videoNote?.Contains("AI estimates") == true,
    "A video recipe must note that caption amounts are estimates.");
Check(RecipeWarnings.CaptionAmountWarnings(quantifiedCaption, "video", groundedIngredients).Count == 0,
    "A video recipe must not warn about caption extraction.");
Check(RecipeWarnings.CaptionAmountNote(quantifiedCaption, null) is not null,
    "Recipes without a source marker must note caption estimates.");
Check(RecipeWarnings.CaptionAmountNote(quantifiedCaption, "edited") is null,
    "Edited recipes must not get the caption estimate note.");
var missingAmounts = new[] { new Ingredient("beef", null), new Ingredient("butter", null) };
Check(RecipeWarnings.CaptionAmountWarnings(quantifiedCaption, "caption", missingAmounts).Single().Contains("were not extracted"),
    "A caption recipe without amounts must warn that quantities were not extracted.");
Check(RecipeWarnings.CaptionAmountWarnings(quantifiedCaption, "caption", groundedIngredients).Count == 0,
    "Amounts matching the caption must not warn.");
Check(RecipeWarnings.CaptionAmountWarnings("A plain caption with no units.", "video", groundedIngredients).Count == 0,
    "A caption without quantities must not warn.");

var captionAmounts = "Beef stroganoff\n300 g beef\n2 tbsp butter";
var labelled = AmountSource.Label(captionAmounts, false,
    [new Ingredient("beef", "300 g"), new Ingredient("butter", "1 tbsp"), new Ingredient("salt", null)]);
Check(labelled[0].AmountSource == AmountSource.Caption,
    "An amount next to its caption ingredient must be labelled as coming from the caption.");
Check(labelled[1].AmountSource == AmountSource.Estimated,
    "An amount the caption does not state must be labelled as an estimate.");
Check(labelled[2].AmountSource is null, "An unknown amount must stay unlabelled.");
Check(AmountSource.Label(null, false, [new Ingredient("beef", "300 g")])[0].AmountSource == AmountSource.Estimated,
    "Without a caption every known amount is an estimate.");
Check(AmountSource.Label(captionAmounts, true, [new Ingredient("beef", "300 g")])[0].AmountSource == AmountSource.Caption,
    "A caption method supplies its amounts directly.");

Console.WriteLine("Recipe validation checks passed.");

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EzCooking.Api.Processing;

public static class OpenRouterModel
{
    public static async Task<JsonElement> VerifyFreeModel(HttpClient client, string model, string modality, CancellationToken ct)
    {
        if (!model.EndsWith(":free", StringComparison.Ordinal))
        {
            throw new ProcessingException("Only explicitly free OpenRouter models are allowed.");
        }

        using var response = await client.GetAsync("https://openrouter.ai/api/v1/models", ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProcessingException("Could not verify the OpenRouter model catalog. Retry later.");
        }

        using var catalog = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var entry = catalog.RootElement.GetProperty("data").EnumerateArray()
            .FirstOrDefault(e => e.GetProperty("id").GetString() == model);
        if (entry.ValueKind == JsonValueKind.Undefined ||
            !entry.GetProperty("architecture").GetProperty("input_modalities").EnumerateArray().Any(v => v.GetString() == modality) ||
            !entry.GetProperty("pricing").TryGetProperty("prompt", out _) ||
            !entry.GetProperty("pricing").TryGetProperty("completion", out _) ||
            entry.GetProperty("pricing").EnumerateObject().Any(p => p.Value.ValueKind != JsonValueKind.String ||
                !decimal.TryParse(p.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var price) || price != 0))
        {
            throw new ProcessingException($"The selected model is unavailable, does not accept {modality}, or is not confirmed free.");
        }

        return entry.Clone();
    }

    public static bool CanDisableReasoning(JsonElement entry) =>
        entry.TryGetProperty("supported_parameters", out var parameters) && parameters.ValueKind == JsonValueKind.Array &&
        parameters.EnumerateArray().Any(p => p.GetString() == "reasoning") &&
        (!entry.TryGetProperty("reasoning", out var reasoning) || reasoning.ValueKind != JsonValueKind.Object ||
         !reasoning.TryGetProperty("mandatory", out var mandatory) || mandatory.ValueKind != JsonValueKind.True);

    public static Dictionary<string, object> Routing(string providerVariable)
    {
        var routing = new Dictionary<string, object>
        {
            ["max_price"] = new { prompt = 0, completion = 0, request = 0, image = 0 },
            ["allow_fallbacks"] = false
        };
        var provider = Environment.GetEnvironmentVariable(providerVariable);
        if (!string.IsNullOrWhiteSpace(provider))
        {
            if (!Regex.IsMatch(provider, "^[a-z0-9-]{1,80}$", RegexOptions.CultureInvariant))
            {
                throw new ProcessingException($"{providerVariable} must be a valid provider slug.");
            }

            routing["only"] = new[] { provider };
        }
        return routing;
    }
}

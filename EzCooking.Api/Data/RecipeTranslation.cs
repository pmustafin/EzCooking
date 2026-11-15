namespace EzCooking.Api.Data;

public sealed class RecipeTranslation
{
    public Guid RecipeId
    {
        get; set;
    }
    public int RecipeVersion
    {
        get; set;
    }
    public string Status { get; set; } = null!;
    public string? Document
    {
        get; set;
    }
    public string? Error
    {
        get; set;
    }
    public DateTimeOffset UpdatedAt
    {
        get; set;
    }
}

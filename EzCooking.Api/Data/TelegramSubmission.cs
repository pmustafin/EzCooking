namespace EzCooking.Api.Data;

public sealed class TelegramSubmission
{
    public Guid Id
    {
        get; set;
    }
    public string SourceUrl { get; set; } = null!;
    public long TelegramMessageId
    {
        get; set;
    }
    public DateTimeOffset CreatedAt
    {
        get; set;
    }
    public string State { get; set; } = null!;
    public Guid? RecipeId
    {
        get; set;
    }
    public string? LastNotifiedStatus
    {
        get; set;
    }
}

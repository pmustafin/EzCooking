namespace EzCooking.Api.Data;

public sealed class RecipeEntity
{
    public Guid Id { get; set; }
    public string? SourceUrl { get; set; }
    public string Status { get; set; } = null!;
    public string? Error { get; set; }
    public string? Document { get; set; }
    public int Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool AudioConsent { get; set; }
    public bool Dismissed { get; set; }
    public string? UploadSha256 { get; set; }
}

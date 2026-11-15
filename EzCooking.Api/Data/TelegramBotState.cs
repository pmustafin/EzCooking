namespace EzCooking.Api.Data;

public sealed class TelegramBotState
{
    public bool Singleton
    {
        get; set;
    }
    public long BotId
    {
        get; set;
    }
    public long? UserId
    {
        get; set;
    }
    public long NextUpdate
    {
        get; set;
    }
    public DateTimeOffset LastUpdateAt
    {
        get; set;
    }
}

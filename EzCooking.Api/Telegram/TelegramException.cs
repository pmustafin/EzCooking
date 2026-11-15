using System.Net;

namespace EzCooking.Api.Telegram;

public sealed class TelegramException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

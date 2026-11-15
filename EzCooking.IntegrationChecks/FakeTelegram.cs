using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

sealed class FakeTelegram : HttpMessageHandler
{
    private readonly ConcurrentQueue<string> _updates = new();
    public int RejectNextCallback;
    public ConcurrentQueue<string> Messages { get; } = new();
    public void Add(string update) => _updates.Enqueue(update);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var method = request.RequestUri!.AbsolutePath.Split('/').Last();
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        string result;
        switch (method)
        {
            case "getMe":
                result = """{"id":1234}""";
                break;
            case "getWebhookInfo":
                result = """{"url":""}""";
                break;
            case "getUpdates":
                if (!_updates.TryDequeue(out var update))
                {
                    await Task.Delay(40, ct);
                    result = "[]";
                }
                else
                {
                    result = "[" + update + "]";
                }

                break;
            case "sendMessage":
                using (var json = JsonDocument.Parse(body))
                {
                    if (json.RootElement.TryGetProperty("reply_markup", out var markup) && markup.ValueKind == JsonValueKind.Null)
                    {
                        return new HttpResponseMessage(HttpStatusCode.BadRequest);
                    }
                }

                Messages.Enqueue(body);
                result = """{"message_id":1}""";
                break;
            case "answerCallbackQuery":
                if (Interlocked.Exchange(ref RejectNextCallback, 0) == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest);
                }

                result = "true";
                break;
            default:
                throw new InvalidOperationException("Unexpected Telegram method.");
        }
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"ok\":true,\"result\":" + result + "}", Encoding.UTF8, "application/json")
        };
    }
}

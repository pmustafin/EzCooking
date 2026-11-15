using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

public sealed class FakeOpenRouter : HttpMessageHandler, IHttpClientFactory
{
    public int Requests
    {
        get; private set;
    }
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public decimal Cost
    {
        get; set;
    }
    public bool ChangeAmount
    {
        get; set;
    }
    public TaskCompletionSource? Started
    {
        get; set;
    }
    public TaskCompletionSource? Continue
    {
        get; set;
    }

    public HttpClient CreateClient(string name) => new(this, false);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsolutePath == "/api/v1/models")
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    data = new[] { new { id = "test/model:free", architecture = new { input_modalities = new[] { "text" } },
                        pricing = new { prompt = "0", completion = "0" } } }
                })
            };
        }

        Requests++;
        using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var root = payload.RootElement;
        var maxPrice = root.GetProperty("provider").GetProperty("max_price");
        if (root.GetProperty("model").GetString() != "test/model:free" ||
            root.GetProperty("provider").GetProperty("allow_fallbacks").GetBoolean() ||
            maxPrice.GetProperty("prompt").GetInt32() != 0 ||
            maxPrice.GetProperty("completion").GetInt32() != 0 ||
            maxPrice.GetProperty("request").GetInt32() != 0 ||
            root.GetProperty("messages")[1].GetProperty("content").ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("The translation request did not enforce free-only text routing.");
        }
        if (Started is not null && Continue is not null)
        {
            Started.TrySetResult();
            await Continue.Task.WaitAsync(cancellationToken);
        }
        if (Status != HttpStatusCode.OK)
        {
            return new HttpResponseMessage(Status);
        }

        var translated = ChangeAmount
            ? """{"items":["Картофель","картофель","3 кг","соль","Запекайте 20 минут при 200°C.","Количество приблизительное.","ужин"]}"""
            : """{"items":["Картофель","картофель","2 кг","соль","Запекайте 20 минут при 200°C.","Количество приблизительное.","ужин"]}""";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                choices = new[] { new { finish_reason = "stop", message = new { content = translated } } },
                usage = new
                {
                    cost = Cost
                }
            })
        };
    }
}

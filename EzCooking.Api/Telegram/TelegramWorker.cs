using System.Net.Http.Json;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using EzCooking.Api.Data;
using EzCooking.Api.Recipes;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EzCooking.Api.Telegram;

public sealed partial class TelegramWorker : BackgroundService
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly DbContextOptions<EzCookingDbContext> _contextOptions;
    private readonly IRecipeSubmissionService _submissions;
    private readonly ILogger<TelegramWorker> _logger;
    private readonly HttpClient _client;
    private string _token = "";
    private string? _pairingCode;
    private DateTimeOffset _pairingExpires;
    private long _botId;

    public TelegramWorker(NpgsqlDataSource db, IRecipeSubmissionService submissions, ILogger<TelegramWorker> logger)
        : this(db, submissions, logger, new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(35) }) { }

    public TelegramWorker(NpgsqlDataSource db, IRecipeSubmissionService submissions, ILogger<TelegramWorker> logger, HttpClient client)
    {
        _contextOptions = new DbContextOptionsBuilder<EzCookingDbContext>().UseNpgsql(db).Options;
        _submissions = submissions;
        _logger = logger;
        _client = client;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")?.Trim() ?? "";
        if (_token.Length == 0)
        {
            return;
        }

        if (!TokenPattern().IsMatch(_token))
        {
            _logger.LogError("Telegram bot token has an invalid format. Check TELEGRAM_BOT_TOKEN.");
            return;
        }
        try
        {
            while (true)
            {
                try
                {
                    var me = await Call("getMe", new
                    {
                    }, stoppingToken);
                    _botId = me.GetProperty("id").GetInt64();
                    var webhook = await Call("getWebhookInfo", new
                    {
                    }, stoppingToken);
                    if (!string.IsNullOrEmpty(webhook.GetProperty("url").GetString()))
                    {
                        _logger.LogError("Telegram bot has a webhook configured. Remove it yourself before using long polling; EzCooking will not modify it.");
                        return;
                    }
                    await Initialize(stoppingToken);
                    break;
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or NpgsqlException or DbUpdateException or TelegramException or TaskCanceledException or InvalidOperationException or KeyNotFoundException or FormatException)
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(stoppingToken);
                    }

                    _logger.LogWarning("Telegram bot setup failed ({Type}); retrying in 30 seconds. Check token, connectivity and bot identity.", ex.GetType().Name);
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
            if ((await State(stoppingToken)).UserId is null)
            {
                _pairingCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                _pairingExpires = DateTimeOffset.UtcNow.AddMinutes(15);
                Console.WriteLine($"Telegram pairing: send /pair {_pairingCode} to your bot in a private chat within 15 minutes. Never share this code.");
            }
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Notify(stoppingToken);
                    await using var context = new EzCookingDbContext(_contextOptions);
                    await context.TelegramBotStates.Where(s => s.Singleton && s.LastUpdateAt < DateTimeOffset.UtcNow.AddDays(-6))
                        .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.NextUpdate, 0)
                            .SetProperty(s => s.LastUpdateAt, DateTimeOffset.UtcNow), stoppingToken);

                    var state = await State(stoppingToken);
                    var updates = await Call("getUpdates", new
                    {
                        offset = state.Offset,
                        timeout = 5,
                        allowed_updates = new[] { "message", "callback_query" }
                    }, stoppingToken);
                    foreach (var update in updates.EnumerateArray())
                    {
                        var id = update.GetProperty("update_id").GetInt64();
                        await Handle(update, stoppingToken);
                        var next = id + 1;
                        await context.TelegramBotStates.Where(s => s.Singleton)
                            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.NextUpdate, s => Math.Max(s.NextUpdate, next))
                                .SetProperty(s => s.LastUpdateAt, DateTimeOffset.UtcNow), stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or NpgsqlException or DbUpdateException or TelegramException or TaskCanceledException or InvalidOperationException or KeyNotFoundException or FormatException)
                {
                    // The Telegram URL contains the bot token; never log exception details.
                    _logger.LogWarning("Telegram communication or storage failed ({Reason}); retrying in 10 seconds.",
                        ex is TelegramException telegram ? telegram.Message : ex.GetType().Name);
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NpgsqlException or DbUpdateException or TelegramException or TaskCanceledException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            _logger.LogError("Telegram bot could not start ({Type}). Check connectivity, token, webhook and bot pairing configuration.", ex.GetType().Name);
        }
    }

    private async Task Initialize(CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO telegram_bot_state (singleton,bot_id) VALUES (true,{_botId})
            ON CONFLICT (singleton) DO UPDATE SET bot_id=EXCLUDED.bot_id,next_update=0,last_update_at=now()
            WHERE telegram_bot_state.user_id IS NULL AND telegram_bot_state.bot_id <> EXCLUDED.bot_id;
            """, ct);
        var state = await context.TelegramBotStates.AsNoTracking().SingleAsync(s => s.Singleton, ct);
        if (state.BotId != _botId)
        {
            throw new TelegramException("The configured bot differs from the paired bot.");
        }
    }

    private async Task<(long? UserId, long Offset)> State(CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        var state = await context.TelegramBotStates.AsNoTracking().SingleOrDefaultAsync(s => s.Singleton, ct);
        if (state is null)
        {
            throw new TelegramException("Telegram state is missing.");
        }

        return (state.UserId, state.NextUpdate);
    }

    private async Task Handle(JsonElement update, CancellationToken ct)
    {
        if (update.TryGetProperty("message", out var message))
        {
            if (!message.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("type", out var type) ||
                type.GetString() != "private" || !message.TryGetProperty("from", out var from) ||
                !from.TryGetProperty("id", out var sender) || !chat.TryGetProperty("id", out var chatId) ||
                sender.GetInt64() != chatId.GetInt64() || !message.TryGetProperty("text", out var textElement) ||
                textElement.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var user = sender.GetInt64();
            var text = textElement.GetString()!.Trim();
            var state = await State(ct);
            if (state.UserId is null)
            {
                if (_pairingCode is null || DateTimeOffset.UtcNow > _pairingExpires ||
                    !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(text),
                        System.Text.Encoding.UTF8.GetBytes("/pair " + _pairingCode)))
                {
                    return;
                }

                await using var context = new EzCookingDbContext(_contextOptions);
                if (await context.TelegramBotStates.Where(s => s.Singleton && s.UserId == null)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.UserId, user), ct) == 1)
                {
                    _pairingCode = null;
                }

                await Send(user, "Paired. Send a public Instagram reel link to prepare a recipe.", null, ct);
                return;
            }
            if (state.UserId != user)
            {
                return;
            }

            if (text.StartsWith("/pair ", StringComparison.Ordinal))
            {
                await Send(user, "This account is already paired.", null, ct);
                return;
            }
            if (text == "/disconnect")
            {
                await using var context = new EzCookingDbContext(_contextOptions);
                await using var transaction = await context.Database.BeginTransactionAsync(ct);
                await context.TelegramSubmissions.ExecuteDeleteAsync(ct);
                await context.TelegramBotStates.Where(s => s.Singleton && s.UserId == user)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.UserId, (long?)null), ct);
                await transaction.CommitAsync(ct);
                _pairingCode = null;
                await Send(user, "Disconnected. Recipes are unchanged. Restart EzCooking to generate a new pairing code.", null, ct);
                return;
            }
            if (text is "/start" or "/help")
            {
                await Send(user, "Send a public Instagram reel link. You will be asked to consent before cloud analysis. Video uploads are not supported here yet.", null, ct);
                return;
            }
            var matches = UrlPattern().Matches(text);
            var urls = matches.Select(m => ReelSource.Canonicalize(m.Value.TrimEnd('.', ',', ')', ']', '>')))
                .Where(url => url is not null).Distinct(StringComparer.Ordinal).ToArray();
            if (urls.Length != 1)
            {
                await Send(user, "Send one public Instagram reel link. If sharing does not include a link, copy the reel link and send it here.", null, ct);
                return;
            }
            var messageId = message.GetProperty("message_id").GetInt64();
            var submission = await SavePending(messageId, urls[0]!, ct);
            await Send(user, $"Reel: {urls[0]}\n\nThis sends video frames and any public caption to OpenRouter and its selected provider. Telegram receives the link and status messages. Only proceed if you have permission to use this video. Audio is not sent.", submission, ct);
        }
        else if (update.TryGetProperty("callback_query", out var callback))
        {
            var state = await State(ct);
            if (!callback.TryGetProperty("from", out var from) || !from.TryGetProperty("id", out var sender) ||
                state.UserId != sender.GetInt64() || !callback.TryGetProperty("message", out var callbackMessage) ||
                !callbackMessage.TryGetProperty("chat", out var chat) || chat.GetProperty("type").GetString() != "private" ||
                chat.GetProperty("id").GetInt64() != sender.GetInt64() ||
                !callback.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var value = data.GetString()!;
            if (value.Length != 34 || value[1] != ':' || !Guid.TryParseExact(value[2..], "N", out var submission))
            {
                return;
            }

            var response = value[0] switch
            {
                'y' => await Consent(submission, ct),
                'n' => await Cancel(submission, ct),
                _ => "This action is invalid."
            };
            await Send(sender.GetInt64(), response, null, ct);
            try
            {
                await Call("answerCallbackQuery", new
                {
                    callback_query_id = callback.GetProperty("id").GetString(),
                    text = response
                }, ct);
            }
            catch (TelegramException ex) when (ex.StatusCode == HttpStatusCode.BadRequest)
            {
                _logger.LogInformation("Telegram could not acknowledge the confirmation (HTTP 400); the result was sent as a message.");
            }
        }
    }

    private async Task<Guid> SavePending(long messageId, string source, CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        var id = Guid.NewGuid();
        var ids = await context.Database.SqlQuery<Guid>($"""
            INSERT INTO telegram_submissions(id,source_url,telegram_message_id)
            VALUES ({id},{source},{messageId}) ON CONFLICT (telegram_message_id)
            DO UPDATE SET telegram_message_id=EXCLUDED.telegram_message_id RETURNING id AS "Value"
            """).ToListAsync(ct);
        return ids.Single();
    }

    private async Task<string> Consent(Guid id, CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        await using var tx = await context.Database.BeginTransactionAsync(ct);
        await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1287583294)", ct);
        var rows = await context.TelegramSubmissions.FromSqlInterpolated($"""
            SELECT * FROM telegram_submissions WHERE id={id} FOR UPDATE
            """).ToListAsync(ct);
        var submission = rows.SingleOrDefault();
        if (submission is null)
        {
            return "This request is no longer available.";
        }
        if (submission.State == "queued")
        {
            return "This request is already queued.";
        }

        if (submission.State == "duplicate")
        {
            return "This reel is already in your collection or processing list.";
        }

        if (submission.State != "pending" || DateTimeOffset.UtcNow - submission.CreatedAt > TimeSpan.FromHours(24))
        {
            return "This request was cancelled or expired. Send the reel again.";
        }

        RecipeResponse recipe;
        try
        {
            recipe = await _submissions.QueueReel(submission.SourceUrl, true, false, context, ct);
        }
        catch (DuplicateRecipeException)
        {
            submission.State = "duplicate";
            await context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return "This reel is already in your collection or processing list. Retry or delete the existing item in EzCooking.";
        }
        submission.State = "queued";
        submission.RecipeId = recipe.Id;
        submission.LastNotifiedStatus = "queued";
        await context.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return "Consent recorded. Your reel is queued; I will send a status update.";
    }

    private async Task<string> Cancel(Guid id, CancellationToken ct)
    {
        await using var context = new EzCookingDbContext(_contextOptions);
        var count = await context.TelegramSubmissions.Where(s => s.Id == id && s.State == "pending")
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.State, "cancelled"), ct);
        return count == 1 ? "Cancelled. Nothing was sent for analysis." : "This request was already handled or is unavailable.";
    }

    private async Task Notify(CancellationToken ct)
    {
        var user = (await State(ct)).UserId;
        if (user is null)
        {
            return;
        }

        await using var context = new EzCookingDbContext(_contextOptions);
        var pending = await (from submission in context.TelegramSubmissions.AsNoTracking()
                             join recipe in context.Recipes.AsNoTracking() on submission.RecipeId equals recipe.Id
                             where submission.State == "queued" &&
                                 (recipe.Status == "processing" || recipe.Status == "complete" || recipe.Status == "failed") &&
                                 submission.LastNotifiedStatus != recipe.Status
                             orderby submission.CreatedAt
                             select new
                             {
                                 submission.Id,
                                 Source = submission.SourceUrl,
                                 recipe.Status,
                                 recipe.Error
                             })
            .Take(20).ToListAsync(ct);

        foreach (var item in pending)
        {
            var text = item.Status switch
            {
                "processing" => "Your reel is being processed.",
                "complete" => "Your recipe is ready. Open EzCooking on your computer to review it and its media.",
                _ => $"Your reel needs attention: {item.Error ?? "Processing failed."} Open EzCooking to retry or upload a video."
            };
            await Send(user.Value, $"{text}\n\n{item.Source}", null, ct);
            await context.TelegramSubmissions.Where(s => s.Id == item.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.LastNotifiedStatus, item.Status), ct);
        }
    }

    private async Task Send(long user, string text, Guid? submission, CancellationToken ct)
    {
        var body = new Dictionary<string, object> { ["chat_id"] = user, ["text"] = text };
        if (submission is not null)
        {
            body["reply_markup"] = new
            {
                inline_keyboard = new[] { new[] {
                new { text = "I consent — create recipe", callback_data = $"y:{submission:N}" },
                new { text = "Cancel", callback_data = $"n:{submission:N}" }
            } }
            };
        }
        await Call("sendMessage", body, ct);
    }

    private async Task<JsonElement> Call(string method, object body, CancellationToken ct)
    {
        using var response = await _client.PostAsJsonAsync($"https://api.telegram.org/bot{_token}/{method}", body, _json, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new TelegramException($"Telegram returned HTTP {(int)response.StatusCode}.", response.StatusCode);
        }

        using var result = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        if (!result.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True ||
            !result.RootElement.TryGetProperty("result", out var value))
        {
            throw new TelegramException("Telegram returned an invalid response.");
        }

        return value.Clone();
    }

    [GeneratedRegex(@"^\d{4,20}:[A-Za-z0-9_-]{20,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
    [GeneratedRegex(@"https://(?:www\.)?instagram\.com/reel/[A-Za-z0-9_-]+/?(?:\?[A-Za-z0-9_=&%-]+)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();
}

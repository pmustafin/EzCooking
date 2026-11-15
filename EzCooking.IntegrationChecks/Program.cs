using EzCooking.Api.Configuration;
using EzCooking.Api.Data;
using EzCooking.Api.Media;
using EzCooking.Api.Processing;
using EzCooking.Api.Recipes;
using EzCooking.Api.Telegram;
using EzCooking.Api.Translation;
using EzCooking.RecipeValidation;
using Npgsql;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;

var testDatabase = Environment.GetEnvironmentVariable("EZCOOKING_TEST_DATABASE");
var testPassword = Environment.GetEnvironmentVariable("EZCOOKING_TEST_PASSWORD");
var testPortValue = Environment.GetEnvironmentVariable("EZCOOKING_TEST_PORT");
if (string.IsNullOrWhiteSpace(testDatabase) ||
    !System.Text.RegularExpressions.Regex.IsMatch(testDatabase, "^ezcooking_test_[a-z0-9_]+$") ||
    string.IsNullOrWhiteSpace(testPassword) ||
    !int.TryParse(testPortValue, out var testPort) || testPort is < 1 or > 65535 || testPort == 55432)
{
    throw new InvalidOperationException("Set EZCOOKING_TEST_DATABASE to a disposable database named ezcooking_test_*, EZCOOKING_TEST_PASSWORD, and EZCOOKING_TEST_PORT to a dedicated port other than 55432.");
}

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var activity = new ProcessingActivity();
var activityId = Guid.NewGuid();
var activityRecipe = new RecipeResponse(activityId, null, "processing", null, null, 0,
    DateTimeOffset.UtcNow, [], false);
activity.Start(activityId);
activity.Add(activityId, "Extracting photos.");
var activitySnapshot = activity.Attach(activityRecipe);
Check(activitySnapshot.Activity?.Count == 2, "Processing milestones are missing.");
Check(activity.Attach(activityRecipe with
{
    Id = Guid.NewGuid()
}).Activity?.Count == 0,
    "Activity leaked to a different job.");
Check(activity.Attach(activityRecipe with
{
    Status = "failed"
}).Activity?.Count == 0,
    "A failed job retained active progress.");
for (var i = 0; i < 20; i++)
{
    activity.Add(activityId, $"Step {i}");
}

Check(activity.Attach(activityRecipe).Activity?.Count == 12 && activitySnapshot.Activity?.Count == 2,
    "Activity history must be bounded and snapshots immutable.");
activity.Clear(activityId);
Check(activity.Attach(activityRecipe).Activity?.Count == 0, "Finishing did not clear activity.");
activity.Start(activityId);
Check(activity.Attach(activityRecipe).Activity?.Count == 1, "A retry retained earlier activity.");
activity.Clear(activityId);
Console.WriteLine("Processing activity isolation, snapshots and retry checks passed.");

var translationSource = new RecipeDocument("Roasted potatoes",
    [new Ingredient("potatoes", "2 kg"), new Ingredient("salt", null)],
    [new Step("Bake for 20 minutes at 200°C.", null)],
    ["The amount is estimated."], ["dinner"], "", [], "", null, "", null, null, null, null);
var translationTexts = LocalTranslation.Texts(translationSource);
Check(translationTexts.SequenceEqual(["Roasted potatoes", "potatoes", "2 kg", "salt",
    "Bake for 20 minutes at 200°C.", "The amount is estimated.", "dinner"]),
    "Translation fields must retain their order and omit unknown amounts.");
Check(LocalTranslation.Valid(translationTexts, ["Картофель", "картофель", "2 кг", "соль",
    "Запекайте 20 минут при 200°C.", "Количество приблизительное.", "ужин"]),
    "Valid translations were rejected.");
Check(LocalTranslation.Valid(translationTexts, ["Картофель", "картофель", "2 кг", "соль",
    "Запекайте при 200°C в течение 20 минут.", "Количество приблизительное.", "ужин"]),
    "Numbers may appear in a different order in translation.");
Check(!LocalTranslation.Valid(translationTexts, ["Картофель"]), "Missing translation fields were accepted.");
Check(!LocalTranslation.Valid(translationTexts, ["Картофель", "картофель", "3 кг", "соль",
    "Запекайте 20 минут при 200°C.", "Количество приблизительное.", "ужин"]),
    "A changed amount was accepted.");
Check(!LocalTranslation.Valid(translationTexts, ["Картофель", "картофель", "2 кг", "соль",
    "Запекайте 20 минут при 180°C.", "Количество приблизительное.", "ужин"]),
    "A changed temperature was accepted.");
Check(!LocalTranslation.Valid(translationTexts, ["Картофель", "", "2 кг", "соль",
    "Запекайте 20 минут при 200°C.", "Количество приблизительное.", "ужин"]),
    "An empty translated field was accepted.");
Console.WriteLine("Local translation structure and number checks passed.");
if (Environment.GetEnvironmentVariable("EZCOOKING_TEST_LOCAL_TRANSLATION") == "true")
{
    var translator = new LocalTranslation();
    var russian = await translator.Translate(translationSource, 1, "ru", CancellationToken.None);
    Check(russian.Language == "ru" && russian.Version == 1 && LocalTranslation.Valid(translationTexts, russian.Items),
        "English-to-Russian local translation failed validation.");
    var russianSource = translationSource with
    {
        Title = "Запечённый картофель",
        Ingredients = [new Ingredient("картофель", "2 кг"), new Ingredient("соль", null)],
        Steps = [new Step("Запекайте 20 минут при 200°C.", null)],
        Uncertainties = ["Количество приблизительное."],
        Tags = ["ужин"]
    };
    var english = await translator.Translate(russianSource, 1, "en", CancellationToken.None);
    Check(english.Language == "en" && english.Version == 1 &&
        LocalTranslation.Valid(LocalTranslation.Texts(russianSource), english.Items),
        "Russian-to-English local translation failed validation.");
    Console.WriteLine("Synthetic local-model translations passed in both directions. Review remains necessary.");
}

var connection = new NpgsqlConnectionStringBuilder
{
    Host = "127.0.0.1",
    Port = testPort,
    Database = testDatabase,
    Username = "ezcooking",
    Password = testPassword
}.ConnectionString;
await using var db = NpgsqlDataSource.Create(connection);
await using (var marker = db.CreateCommand("SELECT coalesce(shobj_description(oid, 'pg_database'), '') FROM pg_database WHERE datname=current_database()"))
{
    if ((string?)await marker.ExecuteScalarAsync() != "ezcooking-disposable-integration-checks")
    {
        throw new InvalidOperationException("The selected database is not marked for disposable integration checks.");
    }
}
var store = new RecipeStore(db);
var submissions = new RecipeSubmissionService(store);
if (Environment.GetEnvironmentVariable("EZCOOKING_TEST_DATABASE") is not null)
{
    await store.Initialize(CancellationToken.None);
}

var efOptions = new DbContextOptionsBuilder<EzCookingDbContext>().UseNpgsql(db).Options;
var efRecipeId = Guid.NewGuid();
await using (var context = new EzCookingDbContext(efOptions))
{
    await using var efTransaction = await context.Database.BeginTransactionAsync();
    context.Recipes.Add(new RecipeEntity
    {
        Id = efRecipeId,
        Status = "staging",
        Document = """{"title":"EF mapping check"}""",
        AudioConsent = true
    });
    await context.SaveChangesAsync();
    context.ChangeTracker.Clear();
    var mapped = await context.Recipes.AsNoTracking().SingleAsync(recipe => recipe.Id == efRecipeId);
    Check(mapped.Status == "staging" && mapped.AudioConsent && mapped.Version == 0 &&
        mapped.CreatedAt != default && mapped.Document?.Contains("EF mapping check") == true,
        "EF recipe mapping did not preserve the existing schema.");
    await efTransaction.RollbackAsync();
}
await using (var context = new EzCookingDbContext(efOptions))
{
    Check(!await context.Recipes.AnyAsync(recipe => recipe.Id == efRecipeId), "EF transaction did not roll back.");
}
Console.WriteLine("EF Core mapping and transaction checks passed.");

var compatibilityId = Guid.NewGuid();
await using (var legacy = db.CreateCommand("""
    INSERT INTO recipes (id,source_url,status,document,version,audio_consent)
    VALUES (@id,'https://www.instagram.com/reel/legacy/','complete',
        '{"title":"Existing recipe","ingredients":[],"steps":[]}'::jsonb,7,true)
    """))
{
    legacy.Parameters.AddWithValue("id", compatibilityId);
    await legacy.ExecuteNonQueryAsync();
}
await store.Initialize(CancellationToken.None);
await store.Initialize(CancellationToken.None);
var existing = await store.Get(compatibilityId, CancellationToken.None);
Check(existing is { Status: "complete", Version: 7, AudioConsent: true } &&
    existing.Document!.Contains("Existing recipe"), "Existing recipe did not survive repeated initialization.");
await using (var migrationCount = db.CreateCommand("SELECT count(*) FROM schema_migrations"))
{
    Check((long)(await migrationCount.ExecuteScalarAsync())! == 5, "Migration history changed after repeated initialization.");
}
var editResults = await Task.WhenAll(Enumerable.Range(0, 5).Select(index =>
    store.Edit(compatibilityId, 7, $$"""{"title":"Concurrent edit {{index}}"}""", CancellationToken.None)));
Check(editResults.Count(result => result) == 1, "Concurrent edits did not enforce the version check.");
await using (var cleanupLegacy = db.CreateCommand("DELETE FROM recipes WHERE id=@id"))
{
    cleanupLegacy.Parameters.AddWithValue("id", compatibilityId);
    await cleanupLegacy.ExecuteNonQueryAsync();
}

var claimJob = await store.QueueReel("https://www.instagram.com/reel/ClaimCheck/", false, CancellationToken.None);
var claims = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => store.Claim(CancellationToken.None)));
Check(claims.Count(item => item?.Id == claimJob.Id) == 1 && claims.Count(item => item is not null) == 1,
    "Concurrent workers did not claim the job exactly once.");
await using (var cleanupClaim = db.CreateCommand("DELETE FROM recipes WHERE id=@id"))
{
    cleanupClaim.Parameters.AddWithValue("id", claimJob.Id);
    await cleanupClaim.ExecuteNonQueryAsync();
}
Console.WriteLine("Existing database, migration idempotence, concurrent edits and job claims passed.");

var boundaryUrl = "https://www.instagram.com/reel/BoundaryTest/";
var rollbackSubmissionId = Guid.NewGuid();
await using (var seedSubmission = db.CreateCommand("""
    INSERT INTO telegram_submissions(id,source_url,telegram_message_id)
    VALUES (@id,@url,-101)
    """))
{
    seedSubmission.Parameters.AddWithValue("id", rollbackSubmissionId);
    seedSubmission.Parameters.AddWithValue("url", boundaryUrl);
    await seedSubmission.ExecuteNonQueryAsync();
}
try
{
    await submissions.QueueReel(boundaryUrl, false, false, CancellationToken.None);
    throw new InvalidOperationException("The submission service accepted a reel without consent.");
}
catch (InvalidOperationException ex) when (ex.Message == "Consent is required before cloud analysis.")
{
}
try
{
    await submissions.StageUpload(null, "test-hash", false, false, CancellationToken.None);
    throw new InvalidOperationException("The submission service accepted an upload without consent.");
}
catch (InvalidOperationException ex) when (ex.Message == "Consent is required before cloud analysis.")
{
}
await using (var testContext = new EzCookingDbContext(efOptions))
await using (var transaction = await testContext.Database.BeginTransactionAsync())
{
    var pending = await submissions.QueueReel(boundaryUrl, true, false, testContext, CancellationToken.None);
    Check(pending.Status == "queued" && !pending.AudioConsent, "Transactional submission lost its status or consent.");
    var submission = await testContext.TelegramSubmissions.SingleAsync(item => item.Id == rollbackSubmissionId);
    submission.State = "queued";
    submission.RecipeId = pending.Id;
    await testContext.SaveChangesAsync();
    await transaction.RollbackAsync();
}
await using (var checkRollback = db.CreateCommand("SELECT count(*) FROM recipes WHERE source_url=@url"))
{
    checkRollback.Parameters.AddWithValue("url", boundaryUrl);
    Check((long)(await checkRollback.ExecuteScalarAsync())! == 0, "A rolled-back submission was persisted.");
}
await using (var checkSubmission = db.CreateCommand("SELECT state,recipe_id FROM telegram_submissions WHERE id=@id"))
{
    checkSubmission.Parameters.AddWithValue("id", rollbackSubmissionId);
    await using var reader = await checkSubmission.ExecuteReaderAsync();
    Check(await reader.ReadAsync() && reader.GetString(0) == "pending" && reader.IsDBNull(1),
        "A rolled-back Telegram submission was persisted.");
}
await using (var cleanupSubmission = db.CreateCommand("DELETE FROM telegram_submissions WHERE id=@id"))
{
    cleanupSubmission.Parameters.AddWithValue("id", rollbackSubmissionId);
    await cleanupSubmission.ExecuteNonQueryAsync();
}
var sharedAttempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(async index =>
{
    try
    {
        if (index % 2 == 0)
        {
            return await submissions.QueueReel(boundaryUrl + "?source=web", true, true, CancellationToken.None);
        }

        await using var sharedContext = new EzCookingDbContext(efOptions);
        await using var sharedTransaction = await sharedContext.Database.BeginTransactionAsync();
        var result = await submissions.QueueReel(boundaryUrl, true, false, sharedContext, CancellationToken.None);
        await sharedTransaction.CommitAsync();
        return result;
    }
    catch (DuplicateRecipeException)
    {
        return null;
    }
}));
Check(sharedAttempts.Count(item => item is not null) == 1, "Web and transactional submissions did not share duplicate protection.");
await using (var cleanupBoundary = db.CreateCommand("DELETE FROM recipes WHERE source_url=@url"))
{
    cleanupBoundary.Parameters.AddWithValue("url", boundaryUrl);
    await cleanupBoundary.ExecuteNonQueryAsync();
}
Console.WriteLine("Shared submission consent, rollback, canonicalization and concurrency checks passed.");

if (Environment.GetEnvironmentVariable("EZCOOKING_TEST_DATABASE") is not null)
{
    await using (var seedBot = db.CreateCommand("INSERT INTO telegram_bot_state(singleton,bot_id) VALUES(true,1234)"))
    {
        await seedBot.ExecuteNonQueryAsync();
    }

    var fake = new FakeTelegram();
    using var http = new HttpClient(fake) { Timeout = TimeSpan.FromSeconds(10) };
    var previousToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
    Environment.SetEnvironmentVariable("TELEGRAM_BOT_TOKEN", "1234:abcdefghijklmnopqrstuvwxyz012345");
    using var bot = new TelegramWorker(db, submissions, NullLogger<TelegramWorker>.Instance, http);
    async Task WaitFor(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }
        throw new InvalidOperationException("Timed out waiting for the simulated Telegram workflow.");
    }
    async Task<long> Count(string sql)
    {
        await using var cmd = db.CreateCommand(sql);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
    try
    {
        var console = Console.Out;
        using var pairingOutput = new StringWriter();
        Console.SetOut(pairingOutput);
        await bot.StartAsync(CancellationToken.None);
        try
        {
            await WaitFor(() => Task.FromResult(pairingOutput.ToString().Contains("Telegram pairing:")));
        }
        finally
        {
            Console.SetOut(console);
        }
        var pairingMatch = System.Text.RegularExpressions.Regex.Match(pairingOutput.ToString(), @"/pair ([a-f0-9]{32})");
        Check(pairingMatch.Success, "Pairing code was not generated.");
        fake.Add("""{"update_id":0,"message":{"message_id":10,"chat":{"type":"private","id":200},"from":{"id":200},"text":"/pair wrong"}}""");
        fake.Add(JsonSerializer.Serialize(new
        {
            update_id = 1,
            message = new
            {
                message_id = 11,
                chat = new
                {
                    type = "private",
                    id = 100
                },
                from = new
                {
                    id = 100
                },
                text = "/pair " + pairingMatch.Groups[1].Value
            }
        }));
        await WaitFor(async () => await Count("SELECT count(*) FROM telegram_bot_state WHERE user_id=100") == 1);
        fake.Add("""{"update_id":2,"message":{"message_id":1,"chat":{"type":"private","id":200},"from":{"id":200},"text":"https://www.instagram.com/reel/Unauthorized/"}}""");
        fake.Add("""{"update_id":3,"message":{"message_id":2,"chat":{"type":"group","id":100},"from":{"id":100},"text":"https://www.instagram.com/reel/Group/"}}""");
        fake.Add("""{"update_id":4,"message":{"message_id":3,"chat":{"type":"private","id":100},"from":{"id":100},"text":"https://www.instagram.com/reel/TestReel/?x=1"}}""");
        await WaitFor(() => Task.FromResult(fake.Messages.Any(m => m.Contains("I consent"))));
        Check(await Count("SELECT count(*) FROM recipes") == 0 &&
              await Count("SELECT count(*) FROM telegram_submissions") == 1,
            "Unauthorized or unconfirmed messages must not queue a recipe.");
        using var prompt = JsonDocument.Parse(fake.Messages.First(m => m.Contains("I consent")));
        var consent = prompt.RootElement.GetProperty("reply_markup").GetProperty("inline_keyboard")[0][0]
            .GetProperty("callback_data").GetString()!;
        var unauthorized = JsonSerializer.Serialize(new
        {
            update_id = 5,
            callback_query = new
            {
                id = "u",
                from = new
                {
                    id = 200
                },
                message = new
                {
                    chat = new
                    {
                        type = "private",
                        id = 200
                    }
                },
                data = consent
            }
        });
        fake.Add(unauthorized);
        await WaitFor(async () => await Count("SELECT count(*) FROM telegram_bot_state WHERE next_update >= 6") == 1);
        Check(await Count("SELECT count(*) FROM recipes") == 0, "Another user could consent to a recipe.");
        fake.RejectNextCallback = 1;
        fake.Add(JsonSerializer.Serialize(new
        {
            update_id = 6,
            callback_query = new
            {
                id = "ok",
                from = new
                {
                    id = 100
                },
                message = new
                {
                    chat = new
                    {
                        type = "private",
                        id = 100
                    }
                },
                data = consent
            }
        }));
        await WaitFor(async () => await Count("SELECT count(*) FROM recipes WHERE status='queued' AND source_url='https://www.instagram.com/reel/TestReel/'") == 1);
        fake.Add(JsonSerializer.Serialize(new
        {
            update_id = 7,
            callback_query = new
            {
                id = "again",
                from = new
                {
                    id = 100
                },
                message = new
                {
                    chat = new
                    {
                        type = "private",
                        id = 100
                    }
                },
                data = consent
            }
        }));
        await WaitFor(() => Task.FromResult(fake.Messages.Any(m => m.Contains("already queued"))));
        Check(await Count("SELECT count(*) FROM recipes") == 1, "Repeated consent created a duplicate recipe.");
        fake.Add("""{"update_id":8,"message":{"message_id":8,"chat":{"type":"private","id":100},"from":{"id":100},"text":"https://www.instagram.com/reel/TestReel/"}}""");
        await WaitFor(() => Task.FromResult(fake.Messages.Count(m => m.Contains("I consent")) == 2));
        using var duplicatePrompt = JsonDocument.Parse(fake.Messages.Last(m => m.Contains("I consent")));
        var duplicateConsent = duplicatePrompt.RootElement.GetProperty("reply_markup").GetProperty("inline_keyboard")[0][0]
            .GetProperty("callback_data").GetString()!;
        fake.Add(JsonSerializer.Serialize(new
        {
            update_id = 9,
            callback_query = new
            {
                id = "duplicate",
                from = new
                {
                    id = 100
                },
                message = new
                {
                    chat = new
                    {
                        type = "private",
                        id = 100
                    }
                },
                data = duplicateConsent
            }
        }));
        await WaitFor(() => Task.FromResult(fake.Messages.Any(m => m.Contains("already in your collection"))));
        Check(await Count("SELECT count(*) FROM recipes") == 1, "A new message bypassed duplicate protection.");
        fake.Add("""{"update_id":10,"message":{"message_id":10,"chat":{"type":"private","id":100},"from":{"id":100},"text":"https://www.instagram.com/reel/Cancelled/"}}""");
        await WaitFor(() => Task.FromResult(fake.Messages.Count(m => m.Contains("I consent")) == 3));
        using var cancelPrompt = JsonDocument.Parse(fake.Messages.Last(m => m.Contains("I consent")));
        var cancel = cancelPrompt.RootElement.GetProperty("reply_markup").GetProperty("inline_keyboard")[0][1]
            .GetProperty("callback_data").GetString()!;
        fake.Add(JsonSerializer.Serialize(new
        {
            update_id = 11,
            callback_query = new
            {
                id = "cancel",
                from = new
                {
                    id = 100
                },
                message = new
                {
                    chat = new
                    {
                        type = "private",
                        id = 100
                    }
                },
                data = cancel
            }
        }));
        await WaitFor(() => Task.FromResult(fake.Messages.Any(m => m.Contains("Nothing was sent"))));
        var cancelledConsent = "y" + cancel[1..];
        fake.Add(JsonSerializer.Serialize(new
        {
            update_id = 12,
            callback_query = new
            {
                id = "cancelled",
                from = new
                {
                    id = 100
                },
                message = new
                {
                    chat = new
                    {
                        type = "private",
                        id = 100
                    }
                },
                data = cancelledConsent
            }
        }));
        await WaitFor(() => Task.FromResult(fake.Messages.Any(m => m.Contains("cancelled or expired"))));
        Check(await Count("SELECT count(*) FROM recipes") == 1, "Cancelled consent queued a recipe.");
        fake.Add("""{"update_id":13,"message":{"message_id":13,"chat":{"type":"private","id":100},"from":{"id":100},"text":"https://www.instagram.com/reel/Expired/"}}""");
        await WaitFor(() => Task.FromResult(fake.Messages.Count(m => m.Contains("I consent")) == 4));
        using var expiredPrompt = JsonDocument.Parse(fake.Messages.Last(m => m.Contains("I consent")));
        var expiredConsent = expiredPrompt.RootElement.GetProperty("reply_markup").GetProperty("inline_keyboard")[0][0]
            .GetProperty("callback_data").GetString()!;
        await using (var expire = db.CreateCommand("UPDATE telegram_submissions SET created_at=now()-interval '25 hours' WHERE telegram_message_id=13"))
        {
            await expire.ExecuteNonQueryAsync();
        }

        fake.Add(JsonSerializer.Serialize(new
        {
            update_id = 14,
            callback_query = new
            {
                id = "expired",
                from = new
                {
                    id = 100
                },
                message = new
                {
                    chat = new
                    {
                        type = "private",
                        id = 100
                    }
                },
                data = expiredConsent
            }
        }));
        await WaitFor(async () => await Count("SELECT count(*) FROM telegram_bot_state WHERE next_update >= 15") == 1);
        Check(await Count("SELECT count(*) FROM recipes") == 1, "Expired consent queued a recipe.");
        await using (var failure = db.CreateCommand("UPDATE recipes SET status='failed',error='Synthetic failure.'"))
        {
            await failure.ExecuteNonQueryAsync();
        }

        await WaitFor(() => Task.FromResult(fake.Messages.Any(m => m.Contains("Synthetic failure."))));
        fake.Add("""{"update_id":15,"message":{"message_id":7,"chat":{"type":"private","id":100},"from":{"id":100},"text":"/disconnect"}}""");
        await WaitFor(async () => await Count("SELECT count(*) FROM telegram_bot_state WHERE user_id IS NULL") == 1);
        Check(await Count("SELECT count(*) FROM telegram_submissions") == 0 &&
              await Count("SELECT count(*) FROM recipes") == 1, "Disconnect must remove bot state but preserve recipes.");
        Console.WriteLine("Simulated Telegram authorization, consent, idempotency, status and disconnect checks passed.");
    }
    finally
    {
        await bot.StopAsync(CancellationToken.None);
        Environment.SetEnvironmentVariable("TELEGRAM_BOT_TOKEN", previousToken);
        await using var cleanup = db.CreateCommand("DELETE FROM telegram_submissions; DELETE FROM telegram_bot_state; DELETE FROM recipes WHERE source_url='https://www.instagram.com/reel/TestReel/'");
        await cleanup.ExecuteNonQueryAsync();
    }
}
if (Environment.GetEnvironmentVariable("EZCOOKING_TEST_DATABASE") is not null)
{
    var job = await store.Create(null, false, CancellationToken.None);
    try
    {
        await store.SetStatus(job.Id, "processing", null, CancellationToken.None);
        activity.Start(job.Id);
        activity.Add(job.Id, "Extracted 3 of 12 photos.");
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IRecipeStore>(store);
        builder.Services.AddSingleton<IRecipeSubmissionService>(submissions);
        builder.Services.AddSingleton(activity);
        builder.Services.AddSingleton<LocalTranslation>();
        builder.Services.AddSingleton(new AppPaths(Directory.GetCurrentDirectory()));
        await using var testApp = builder.Build();
        testApp.MapRecipes();
        await testApp.StartAsync();
        try
        {
            var address = testApp.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address) };
            const string apiSource = "https://www.instagram.com/reel/ApiBoundary/";
            using (var noConsent = await client.PostAsJsonAsync("/api/recipes", new CreateRecipeRequest(apiSource, false)))
            {
                Check(noConsent.StatusCode == HttpStatusCode.BadRequest, "The recipe API accepted missing consent.");
            }
            using (var accepted = await client.PostAsJsonAsync("/api/recipes", new CreateRecipeRequest(apiSource + "?a=1", true, true)))
            {
                var acceptedRecipe = await accepted.Content.ReadFromJsonAsync<RecipeResponse>();
                Check(accepted.StatusCode == HttpStatusCode.Created && acceptedRecipe is { Status: "queued", AudioConsent: true } &&
                    acceptedRecipe.SourceUrl == apiSource && accepted.Headers.Location?.ToString() == $"/api/recipes/{acceptedRecipe.Id}",
                    "The recipe API did not return the queued canonical submission.");
            }
            using (var duplicateResponse = await client.PostAsJsonAsync("/api/recipes", new CreateRecipeRequest(apiSource, true)))
            {
                Check(duplicateResponse.StatusCode == HttpStatusCode.Conflict, "The recipe API accepted a duplicate.");
            }
            await using (var cleanupApi = db.CreateCommand("DELETE FROM recipes WHERE source_url=@url"))
            {
                cleanupApi.Parameters.AddWithValue("url", apiSource);
                await cleanupApi.ExecuteNonQueryAsync();
            }
            var single = await client.GetFromJsonAsync<RecipeResponse>($"/api/recipes/{job.Id}");
            var page = await client.GetFromJsonAsync<RecipePage<RecipeResponse>>("/api/processing?dismissed=false&offset=0");
            Check(single?.Activity?.Last().Message == "Extracted 3 of 12 photos." &&
                page?.Items.Single(r => r.Id == job.Id).Activity?.Count == 2,
                "Activity was not returned by both processing APIs.");
            await store.SetStatus(job.Id, "failed", "Test failure.", CancellationToken.None);
            single = await client.GetFromJsonAsync<RecipeResponse>($"/api/recipes/{job.Id}");
            Check(single?.Activity?.Count == 0 && single.Error == "Test failure.",
                "Failure must replace active activity in the API.");
            Console.WriteLine("Processing activity API checks passed.");
        }

        finally
        {
            await testApp.StopAsync();
            activity.Clear(job.Id);
        }
    }
    finally
    {
        await using var cleanup = db.CreateCommand("DELETE FROM recipes WHERE id=@id");
        cleanup.Parameters.AddWithValue("id", job.Id);
        await cleanup.ExecuteNonQueryAsync();
    }
}
if (Environment.GetEnvironmentVariable("EZCOOKING_TEST_LOCAL_TRANSLATION") == "true")
{
    var apiJob = await store.Create(null, false, CancellationToken.None);
    try
    {
        await store.SetStatus(apiJob.Id, "processing", null, CancellationToken.None);
        await store.Complete(apiJob.Id, JsonSerializer.Serialize(translationSource, new JsonSerializerOptions(JsonSerializerDefaults.Web)), CancellationToken.None);
        var saved = (await store.Get(apiJob.Id, CancellationToken.None))!;
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IRecipeStore>(store);
        builder.Services.AddSingleton<IRecipeSubmissionService>(submissions);
        builder.Services.AddSingleton<ProcessingActivity>();
        builder.Services.AddSingleton<LocalTranslation>();
        builder.Services.AddSingleton(new AppPaths(Directory.GetCurrentDirectory()));
        await using var testApp = builder.Build();
        testApp.MapRecipes();
        await testApp.StartAsync();
        try
        {
            var address = testApp.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromMinutes(3) };
            using var translated = await client.PostAsJsonAsync($"/api/recipes/{apiJob.Id}/translation", new TranslateRecipeRequest(saved.Version, "ru"));
            Check(translated.StatusCode == HttpStatusCode.OK, "The translation API failed.");
            var result = await translated.Content.ReadFromJsonAsync<TranslationResponse>();
            Check(result is not null && LocalTranslation.Valid(translationTexts, result.Items), "The translation API returned invalid output.");
            var after = await store.Get(apiJob.Id, CancellationToken.None);
            Check(after?.Version == saved.Version && after.Document == saved.Document, "Translation changed the saved recipe.");
            using var invalid = await client.PostAsJsonAsync($"/api/recipes/{apiJob.Id}/translation", new TranslateRecipeRequest(saved.Version, "fr"));
            Check(invalid.StatusCode == HttpStatusCode.BadRequest, "Invalid translation language was accepted.");
            Check(await store.Edit(apiJob.Id, saved.Version, saved.Document!, CancellationToken.None), "Test edit failed.");
            using var stale = await client.PostAsJsonAsync($"/api/recipes/{apiJob.Id}/translation", new TranslateRecipeRequest(saved.Version, "ru"));
            Check(stale.StatusCode == HttpStatusCode.Conflict, "A stale recipe version was translated.");
            using var missing = await client.PostAsJsonAsync($"/api/recipes/{Guid.NewGuid()}/translation", new TranslateRecipeRequest(1, "ru"));
            Check(missing.StatusCode == HttpStatusCode.NotFound, "A missing recipe did not return 404.");
            Console.WriteLine("Translation API and edit protection checks passed.");
        }
        finally
        {
            await testApp.StopAsync();
        }
    }
    finally
    {
        await using var cleanup = db.CreateCommand("DELETE FROM recipes WHERE id=@id");
        cleanup.Parameters.AddWithValue("id", apiJob.Id);
        await cleanup.ExecuteNonQueryAsync();
    }
}
var created = await store.Create(null, false, CancellationToken.None);
try
{
    Check(!created.AudioConsent, "A new job unexpectedly has audio consent.");
    var reviewCreated = await store.Create(null, false, CancellationToken.None);
    try
    {
        await store.SetStatus(reviewCreated.Id, "processing", null, CancellationToken.None);
        await store.Complete(reviewCreated.Id, """
            {"title":"Sicilian Chicken Pasta","ingredients":[{"name":"pasta","estimatedAmount":"200 g"}],
             "steps":[{"text":"Cook the pasta in boiling water.","timestampSeconds":null}],
             "uncertainties":[],"tags":["Sicilian","chicken","pasta"],"cover":"","frames":[],"video":"","model":"",
             "recipeSource":"video"}
            """, CancellationToken.None);
        var savedReview = await store.Get(reviewCreated.Id, CancellationToken.None);
        Check(savedReview is { Status: "complete" } &&
            savedReview.Warnings.Any(w => w.Contains("chicken") && w.Contains("If it is an ingredient")) &&
            !savedReview.Warnings.Any(w => w.Contains("Sicilian")),
            "Title/tag checks must flag a possible missing ingredient without treating Sicilian as one.");
        Check((await store.Collection("Sicilian", 0, 24, CancellationToken.None)).Items
            .Any(item => item.Id == reviewCreated.Id && item.NeedsReview),
            "Potentially incomplete saved recipes must remain visible and marked for review.");
    }
    finally
    {
        await using var cleanupReview = db.CreateCommand("DELETE FROM recipes WHERE id=@id");
        cleanupReview.Parameters.AddWithValue("id", reviewCreated.Id);
        await cleanupReview.ExecuteNonQueryAsync();
    }
    var audioCreated = await store.Create(null, true, CancellationToken.None);
    try
    {
        Check(audioCreated.AudioConsent && (await store.Get(audioCreated.Id, CancellationToken.None))?.AudioConsent == true,
            "Audio consent was not persisted on creation.");
    }
    finally
    {
        await using var cleanupAudio = db.CreateCommand("DELETE FROM recipes WHERE id=@id");
        cleanupAudio.Parameters.AddWithValue("id", audioCreated.Id);
        await cleanupAudio.ExecuteNonQueryAsync();
    }
    var captionCreated = await store.Create(null, false, CancellationToken.None);
    try
    {
        await store.SetStatus(captionCreated.Id, "processing", null, CancellationToken.None);
        await store.Complete(captionCreated.Id, """
            {"title":"Pasta","ingredients":[{"name":"pasta","estimatedAmount":"3"}],
             "steps":[{"text":"Add 3:1 salt to pasta.","timestampSeconds":null},{"text":"Cook pasta for 10 minutes.","timestampSeconds":null}],
             "uncertainties":[],"tags":[],"cover":"","frames":[],"video":"","model":"",
             "sourceCaption":"1. Add 3:1 salt to pasta.\n2. Cook pasta for 10 minutes.","recipeSource":"caption"}
            """, CancellationToken.None);
        var savedCaption = await store.Get(captionCreated.Id, CancellationToken.None);
        Check(savedCaption?.Warnings.Any(w => w.Contains("matched reliably")) == true,
            "Previously saved unreliable caption amounts must be flagged.");
        Check(!savedCaption!.Warnings.Any(w => w.Contains("cooking time")),
            "Source-provided cooking times must survive persistence.");
    }
    finally
    {
        await using var cleanupCaption = db.CreateCommand("DELETE FROM recipes WHERE id=@id");
        cleanupCaption.Parameters.AddWithValue("id", captionCreated.Id);
        await cleanupCaption.ExecuteNonQueryAsync();
    }
    await using (var defaultConsent = db.CreateCommand("INSERT INTO recipes (id,status) VALUES (@id,'staging') RETURNING audio_consent"))
    {
        var legacyId = Guid.NewGuid();
        defaultConsent.Parameters.AddWithValue("id", legacyId);
        try
        {
            Check((bool)(await defaultConsent.ExecuteScalarAsync())! == false, "The database default must not grant audio consent.");
        }
        finally
        {
            await using var cleanupDefault = db.CreateCommand("DELETE FROM recipes WHERE id=@id");
            cleanupDefault.Parameters.AddWithValue("id", legacyId);
            await cleanupDefault.ExecuteNonQueryAsync();
        }
    }
    await store.SetStatus(created.Id, "failed", "test", CancellationToken.None);
    Check(await store.Dismiss(created.Id, true, CancellationToken.None), "Dismiss failed.");
    Check((await store.Processing(true, 0, 24, CancellationToken.None)).Items.Any(j => j.Id == created.Id), "Dismissed job is missing.");
    Check(!(await store.Processing(false, 0, 24, CancellationToken.None)).Items.Any(j => j.Id == created.Id), "Dismissed job still appears in current jobs.");
    Check(await store.Dismiss(created.Id, false, CancellationToken.None), "Restore failed.");
    Check((await store.Get(created.Id, CancellationToken.None)) is { Status: "failed" }, "Dismissal must not remove the job.");
    Check(await store.Retry(created.Id, true, CancellationToken.None), "Retry with audio consent failed.");
    Check((await store.Get(created.Id, CancellationToken.None))?.AudioConsent == true, "Audio consent was not persisted on retry.");
    await store.SetStatus(created.Id, "failed", "test", CancellationToken.None);
    Check(await store.Retry(created.Id, false, CancellationToken.None), "Retry without audio consent failed.");
    Check((await store.Get(created.Id, CancellationToken.None))?.AudioConsent == false, "Retry did not revoke prior audio consent.");
    await store.SetStatus(created.Id, "processing", null, CancellationToken.None);
    await store.Complete(created.Id, """{"title":"initial"}""", CancellationToken.None);
    var initial = await store.Get(created.Id, CancellationToken.None);
    Check(initial is { Status: "complete", Version: 1 }, "Completed recipe was not persisted.");
    Check(await store.Edit(created.Id, 1, """{"title":"corrected"}""", CancellationToken.None), "Edit failed.");
    Check(!await store.Edit(created.Id, 1, """{"title":"stale"}""", CancellationToken.None), "Stale edit overwrote a newer edit.");
    Check(!await store.Retry(created.Id, true, CancellationToken.None), "Retry must not overwrite an edited recipe.");
    await using var freshDb = NpgsqlDataSource.Create(connection);
    var fresh = await new RecipeStore(freshDb).Get(created.Id, CancellationToken.None);
    Check(fresh is { Version: 2 } && fresh.Document!.Contains("corrected"), "Edit did not survive a fresh database connection.");
    try
    {
        await store.Complete(created.Id, """{"title":"reprocessed"}""", CancellationToken.None);
        throw new InvalidOperationException("Reprocessing overwrote an edited recipe.");
    }
    catch (InvalidOperationException ex) when (ex.Message == "Recipe changed during processing.")
    {
    }
    Console.WriteLine("Audio-consent, persistence and edit-protection checks passed.");
}
finally
{
    await using var cleanup = db.CreateCommand("DELETE FROM recipes WHERE id=@id");
    cleanup.Parameters.AddWithValue("id", created.Id);
    await cleanup.ExecuteNonQueryAsync();
}
if (Environment.GetEnvironmentVariable("EZCOOKING_TEST_DATABASE") is not null)
{
    var sameUrl = "https://www.instagram.com/reel/duplicate-test/";
    var attempts = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
    {
        try
        {
            return await store.CreateUnique(sameUrl, null, false, CancellationToken.None);
        }
        catch (DuplicateRecipeException)
        {
            return null;
        }
    }));
    Check(attempts.Count(x => x is not null) == 1, "Concurrent duplicate reel creations must permit only one.");
    var duplicate = attempts.First(x => x is not null)!;
    Check(!await store.BeginDelete(duplicate.Id, CancellationToken.None), "A staging job must not be deleted.");
    await store.SetStatus(duplicate.Id, "failed", "test", CancellationToken.None);
    Check(await store.Dismiss(duplicate.Id, true, CancellationToken.None), "Test dismissal failed.");
    try
    {
        await store.CreateUnique(sameUrl, null, false, CancellationToken.None);
        throw new InvalidOperationException("Dismissed duplicate was accepted.");
    }
    catch (DuplicateRecipeException)
    {
    }
    Check(await store.BeginDelete(duplicate.Id, CancellationToken.None), "Failed job deletion did not start.");
    await store.FinishDelete(duplicate.Id, CancellationToken.None);
    var again = await store.CreateUnique(sameUrl, null, false, CancellationToken.None);
    Check(again.Id != duplicate.Id, "Deleted reel should be addable again.");
    var hash = await MediaManagement.Hash(new MemoryStream([1, 2, 3]), CancellationToken.None);
    var upload = await store.CreateUnique(null, hash, false, CancellationToken.None);
    try
    {
        await store.CreateUnique(null, hash, false, CancellationToken.None);
        throw new InvalidOperationException("Identical upload was accepted.");
    }
    catch (DuplicateRecipeException)
    {
    }
    await store.SetStatus(upload.Id, "processing", null, CancellationToken.None);
    await store.Complete(upload.Id, """{"title":"initial"}""", CancellationToken.None);
    Check(await store.BeginDelete(upload.Id, CancellationToken.None), "Completed recipe deletion did not start.");
    await store.FinishDelete(upload.Id, CancellationToken.None);
    var uploadAgain = await store.CreateUnique(null, hash, false, CancellationToken.None);
    Check(uploadAgain.Id != upload.Id, "Deleted upload should be addable again.");
    var tempRoot = Path.Combine(Path.GetTempPath(), "ezcooking-test-" + Guid.NewGuid().ToString("N"));
    var paths = new AppPaths(tempRoot);
    var tempId = Guid.NewGuid();
    Directory.CreateDirectory(paths.For(tempId));
    await File.WriteAllTextAsync(Path.Combine(paths.For(tempId), "video.mp4"), "test");
    var child = Path.Combine(paths.For(tempId), "unexpected");
    Directory.CreateDirectory(child);
    try
    {
        MediaManagement.DeleteRecipeMedia(paths, tempId);
        throw new InvalidOperationException("Unexpected nested directory was removed.");
    }
    catch (IOException)
    {
    }
    Check(File.Exists(Path.Combine(paths.For(tempId), "video.mp4")), "Failed media deletion must retain the item.");
    Directory.Delete(child);
    MediaManagement.DeleteRecipeMedia(paths, tempId);
    Check(!Directory.Exists(paths.For(tempId)), "Local recipe media was not deleted.");
    Directory.Delete(paths.Media);
    Directory.Delete(tempRoot);
    Console.WriteLine("Duplicate creation and deletion checks passed.");
    await using (var seed = db.CreateCommand("""
        INSERT INTO recipes (id,status,document,created_at)
        SELECT gen_random_uuid(),'complete',
          jsonb_build_object('title','Collection test ' || n,'ingredients',jsonb_build_array(jsonb_build_object('name','lentils','estimatedAmount','1 cup')),
            'steps',jsonb_build_array(jsonb_build_object('text','Cook the lentils in water.','timestampSeconds',null)),
            'uncertainties',jsonb_build_array(),'tags',jsonb_build_array('weekday'),'cover','/cover','frames',jsonb_build_array(),'video','/video','model','test'),
          now() - (n * interval '1 second')
        FROM generate_series(1,1050) n
        """))
    {
        await seed.ExecuteNonQueryAsync();
    }

    var first = await store.Collection("lentils", 0, 24, CancellationToken.None);
    var second = await store.Collection("lentils", 24, 24, CancellationToken.None);
    Check(first.Items.Count == 24 && first.HasMore && second.Items.Count == 24 &&
          !first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)).Any(), "Collection pagination failed.");
    Check((await store.Collection("weekday", 0, 24, CancellationToken.None)).Items.Count == 24, "Tag search failed.");
    Check((await store.Collection("Collection test 1050", 0, 24, CancellationToken.None)).Items.Count == 1, "Title search failed.");
    await using (var tagSeed = db.CreateCommand("""
        UPDATE recipes SET document=jsonb_set(document,'{tags}',
            CASE WHEN document->>'title'='Collection test 1' THEN '[" WeekDAY "]'::jsonb
                 ELSE '["weekend"]'::jsonb END)
        WHERE document->>'title' IN ('Collection test 1','Collection test 2');
        """))
    {
        await tagSeed.ExecuteNonQueryAsync();
    }

    var taggedFirst = await store.Collection("lentils", 0, 24, CancellationToken.None, "WEEKDAY");
    var taggedSecond = await store.Collection("lentils", 24, 24, CancellationToken.None, "weekday");
    Check(taggedFirst.Items.Count == 24 && taggedFirst.HasMore && taggedSecond.Items.Count == 24 &&
          !taggedFirst.Items.Select(x => x.Id).Intersect(taggedSecond.Items.Select(x => x.Id)).Any() &&
          taggedFirst.Items.All(x => x.Title != "Collection test 2"), "Tag filtering or pagination failed.");
    Check((await store.Collection("Collection test 1", 0, 24, CancellationToken.None, "weekday")).Items.Any(x => x.Title == "Collection test 1") &&
          (await store.Collection("Collection test 1", 0, 24, CancellationToken.None, "week")).Items.Count == 0,
          "Tag filtering must use exact, case-insensitive trimmed matches combined with search.");
    var suggestions = await store.Tags("week", CancellationToken.None);
    Check(suggestions.Count(x => x.Equals("weekday", StringComparison.OrdinalIgnoreCase)) == 1 &&
          suggestions.Contains("weekend"), "Tag suggestions must include distinct matching saved tags.");
    await using (var popularitySeed = db.CreateCommand("""
        UPDATE recipes SET document=jsonb_set(document,'{tags}',
            CASE WHEN document->>'title'='Collection test 2'
                THEN '["weekend","aaa","aaa","aaa","aaa"]'::jsonb
                ELSE (document->'tags') || '["zzshared"]'::jsonb END)
        WHERE document->>'title' IN ('Collection test 1','Collection test 2','Collection test 3');
        """))
    {
        await popularitySeed.ExecuteNonQueryAsync();
    }

    var popularTags = await store.Tags(null, CancellationToken.None, true);
    Check(popularTags[0].Equals("weekday", StringComparison.OrdinalIgnoreCase) &&
          popularTags.ToList().IndexOf("zzshared") < popularTags.ToList().IndexOf("aaa"),
          "Popular tags must count recipes, not repeated tags within a recipe.");
    Check((await store.Tags(null, CancellationToken.None))[0] == "aaa", "Default tag order must remain alphabetical.");
    var builder = WebApplication.CreateSlimBuilder();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.AddSingleton<IRecipeStore>(store);
    builder.Services.AddSingleton<IRecipeSubmissionService>(submissions);
    builder.Services.AddSingleton<ProcessingActivity>();
    builder.Services.AddSingleton<LocalTranslation>();
    builder.Services.AddSingleton(new AppPaths(Directory.GetCurrentDirectory()));
    await using var testApp = builder.Build();
    testApp.MapRecipes();
    await testApp.StartAsync();
    try
    {
        var address = testApp.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var page = await client.GetFromJsonAsync<RecipePage<RecipeSummary>>("/api/collection?search=lentils&tag=weekend&offset=0");
        var apiTags = await client.GetFromJsonAsync<string[]>("/api/collection/tags?search=week");
        var apiPopular = await client.GetFromJsonAsync<string[]>("/api/collection/tags?popular=true");
        Check(page?.Items.Count == 1 && page.Items[0].Title == "Collection test 2" &&
              apiTags is not null && apiTags.Contains("weekend") &&
              apiPopular is not null && apiPopular[0].Equals("weekday", StringComparison.OrdinalIgnoreCase),
              "Collection tag APIs returned incorrect results.");
        using var invalid = await client.GetAsync("/api/collection?tag=" + new string('x', 201));
        Check(invalid.StatusCode == HttpStatusCode.BadRequest, "Oversized tag was accepted.");
    }
    finally
    {
        await testApp.StopAsync();
    }
    Console.WriteLine("Pagination, search and tag filter checks passed with 1,050 recipes.");
}

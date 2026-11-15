using System.Net;
using System.Text.Json.Serialization;
using EzCooking.Api.Configuration;
using EzCooking.Api.Media;
using EzCooking.Api.Processing;
using EzCooking.Api.Recipes;
using EzCooking.Api.Telegram;
using EzCooking.Api.Translation;
using Microsoft.Extensions.FileProviders;
using Npgsql;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
LocalConfig.Load(Path.Combine(root, ".env"));
using var singleInstance = new FileStream(Path.Combine(root, ".ezcooking.lock"),
    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5187");
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 101_000_000);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD")
    ?? throw new InvalidOperationException("Set POSTGRES_PASSWORD in the root .env file.");
var connection = new NpgsqlConnectionStringBuilder
{
    Host = "127.0.0.1",
    Port = 55432,
    Database = "ezcooking",
    Username = "ezcooking",
    Password = password
}.ConnectionString;
builder.Services.AddSingleton(NpgsqlDataSource.Create(connection));
builder.Services.AddSingleton<IRecipeStore, RecipeStore>();
builder.Services.AddSingleton<IRecipeSubmissionService, RecipeSubmissionService>();
builder.Services.AddSingleton<ProcessingActivity>();
builder.Services.AddSingleton<LocalTranslation>();
builder.Services.AddSingleton<RussianTranslationService>();
builder.Services.AddSingleton(new AppPaths(root));
builder.Services.AddHttpClient();
builder.Services.AddHostedService<RecipeWorker>();
builder.Services.AddHostedService<RussianTranslationWorker>();
builder.Services.AddSingleton<IHostedService>(services => new TelegramWorker(
    services.GetRequiredService<NpgsqlDataSource>(), services.GetRequiredService<IRecipeSubmissionService>(),
    services.GetRequiredService<ILogger<TelegramWorker>>()));
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 100_000_000);
var app = builder.Build();
await app.Services.GetRequiredService<IRecipeStore>().Initialize(CancellationToken.None);
await MediaManagement.BackfillUploads(app.Services.GetRequiredService<IRecipeStore>(),
    app.Services.GetRequiredService<AppPaths>(), CancellationToken.None);
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    var host = context.Request.Host.Host;
    if (host is not ("127.0.0.1" or "localhost") ||
        (context.Connection.RemoteIpAddress is { } ip && !IPAddress.IsLoopback(ip)))
    {
        context.Response.StatusCode = 403;
        return;
    }
    if (context.Request.Headers.TryGetValue("Origin", out var origin) &&
        origin.ToString() is not ("http://127.0.0.1:5187" or "http://localhost:5187"))
    {
        context.Response.StatusCode = 403;
        return;
    }
    if (context.Request.Headers["Sec-Fetch-Site"].ToString() is "cross-site")
    {
        context.Response.StatusCode = 403;
        return;
    }
    await next();
});
app.MapOpenApi();
app.MapRecipes();
var web = Path.Combine(root, "EzCooking.Web", "dist");
if (Directory.Exists(web))
{
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new PhysicalFileProvider(web) });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(web) });
}
app.Run();

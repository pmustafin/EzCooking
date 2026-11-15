# EzCooking

A local, single-user recipe notebook for public Instagram reels and videos you
are entitled to use. It runs on Windows with ASP.NET Core, PostgreSQL, React,
yt-dlp and FFmpeg.

## First-time setup on Windows

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0),
   [Node.js 24 LTS](https://nodejs.org/) and [Docker Desktop](https://www.docker.com/products/docker-desktop/).
   Install [FFmpeg](https://ffmpeg.org/download.html) (including `ffmpeg` and
   `ffprobe`) and [yt-dlp](https://github.com/yt-dlp/yt-dlp#installation) and
   add them to your PATH. Alternatively, set `FFMPEG_PATH`, `FFPROBE_PATH`
   and `YTDLP_PATH` in `.env` to the executable paths. The optional local
   `.tools` directory can contain the three executables instead; it is not
   included in the repository.
2. Open PowerShell in the repository folder. Copy the example configuration
   only if you do not already have a `.env`:

   ```powershell
   if (-not (Test-Path .env)) { Copy-Item .env.example .env }
   notepad .env
   ```

   Replace the placeholders with a long, unique `POSTGRES_PASSWORD` and an
   [OpenRouter API key](https://openrouter.ai/keys). Keep this file private;
   never paste the key into chat or commit it. The app reads `.env` itself;
   existing environment variables take precedence. If a database volume
   already exists, changing `.env` does not change its PostgreSQL password.
3. Install frontend dependencies once:

   ```powershell
   npm install --prefix .\EzCooking.Web
   ```

4. Start Docker Desktop and wait for it to be ready. In the same folder run:

   ```powershell
   .\start.ps1
   ```

   The script starts PostgreSQL, builds the frontend and runs the API. Open
   **http://127.0.0.1:5187/** and keep the PowerShell window open.
5. Add a public Instagram reel link or upload a video you are entitled to use.
   Review the consent notice before starting analysis. Frames and any public
   caption are sent to OpenRouter and its selected provider; audio is sent
   only with separate optional consent. See [Privacy and limitations](#privacy-and-limitations).

To stop, press Ctrl+C in the app window, then run `.\stop.ps1` in PowerShell.
This stops PostgreSQL but retains its database volume and local media. To start
again, start Docker Desktop and run `.\start.ps1`; setup and `npm install` are
not normally needed again.

If startup fails, check that Docker Desktop is running, the tools are installed,
and ports 5187 and 55432 are available. If Windows cannot find a tool, restart
PowerShell after updating PATH or configure its executable path in `.env`.
Private or blocked Instagram reels cannot be downloaded; retry or upload a
video instead.

## Code organization

The API is grouped by feature: `Recipes` contains endpoints and persistence,
`Processing` handles recipe generation and progress, `Media` handles stored
files, `Telegram` handles the bot, `Translation` contains the optional local
translation service, and `Configuration` loads local settings and paths.
`Program.cs` wires these components together, while `Migrations` contains the
embedded database migrations. These folders organize the existing application;
they are not independent services.

Recipe validation is a separate shared project. The validation and integration
check projects exercise the existing behavior.
Web and Telegram submissions share the recipe submission service, including
consent and duplicate checks; Telegram records its consent and recipe creation
in the same database transaction. This is a foundation for private collections,
not multi-user authorization: the app remains local and single-user.
Open `EzCooking.sln` in Visual Studio to work with all four C# projects.
The React frontend remains a separate Node.js project.

The versioned SQL migration is applied transactionally on API startup without
dropping existing data. Interrupted jobs are marked failed; retry them after checking the cause.
Only one local application instance and one video job run at a time.

Recipe and Telegram data access uses Entity Framework Core with short-lived
contexts. PostgreSQL-specific SQL is retained for locking, atomic operations
and JSON searches. The existing versioned SQL migrations remain authoritative;
EF does not create or migrate the database.

The collection loads newest recipes first in pages; search covers titles, ingredients and
tags. Filter by one exact tag alongside search. The tag browser shows up to eight
popular tags; type to find others. Tags and their counts are based on saved
recipes and are not changed by browsing. Add videos and track their processing on separate screens. A failed job
can be retried with renewed consent or dismissed.
The interface adapts [Carbon's spacing scale](https://carbondesignsystem.com/elements/spacing/overview/),
neutral color tokens, search guidance and empty-state patterns to the recipe
collection. The components and green accent are custom rather than Carbon React components.
On larger screens the recipe's video is at the top; on smaller screens it appears
below the ingredients and method. The player starts paused and uses the cover as
its poster. Resizing across the layout breakpoint resets video playback.
Use Correct recipe to edit, add, remove or reorder individual ingredients and
instructions. Existing video timestamps follow unchanged steps when reordered;
editing a step clears its timestamp. Source information and uncertainties remain.
The processing view shows recent worker milestones and the last successful
status check. Activity messages are not continuous heartbeats; a long-running
step may not produce updates. Activity history is kept in memory only and is
cleared when a job finishes or the app restarts.
Use Refresh videos to see new Telegram submissions while the processing view is
open; refreshing returns the list to its first page.
Dismissed jobs and their media are retained and can be restored from Processing.
Failed jobs and saved recipes can also be permanently deleted after confirmation;
this removes their local media. Deletion is unavailable while processing.
The same reel URL or byte-identical uploaded file cannot be added again while
an existing job or recipe remains, including dismissed failures. Delete it first
to add it again or to upload a replacement with the same reel link. Uploads are
identified by a SHA-256 hash of their original bytes; re-encoded copies are not
recognized as identical. Existing saved upload files are hashed on startup.

The experimental local translation API remains available, but is not exposed
in the recipe interface. It uses Ollama and requires the `qwen3-vl:4b-instruct`
model; see the API example. Machine translations can change meaning or units,
so verify them against the original before cooking.

## Telegram phone sharing (optional)

Create a bot using Telegram's official [BotFather](https://t.me/BotFather).
Add its token as `TELEGRAM_BOT_TOKEN` in the local `.env`, without posting it
in chat or committing the file. Restart EzCooking, then send `/pair` followed
by the one-time code printed in the EzCooking terminal to your bot **in a
private chat** within 15 minutes. Do not share the code. If it expires, restart
the app to generate another. Pairing is limited to one Telegram account.

Share an Instagram reel link with the bot. If Instagram shares no link, copy
the link and send it instead. For each reel, press the consent button to send
frames and the public caption to OpenRouter and its selected provider. The bot
does not analyze audio or accept uploads. Duplicate reels are rejected; retry
or delete the existing item in the desktop app. The bot reports processing,
completion or failure, and the recipe stays in the local app.

Telegram sees links, consent interactions and status messages. Messages are
not end-to-end encrypted with the bot. Avoid sending private or sensitive
material. The app uses outbound long polling and does not expose a public
webhook. Do not use the same bot token with another poller or an active webhook;
EzCooking refuses an existing webhook rather than changing it. The computer
must be on to process reels; Telegram holds undelivered updates for a limited
time. Consent buttons expire after 24 hours. Notifications may repeat after a
network interruption or restart; queued recipes are protected from duplicates.
Send `/disconnect` in the paired private chat to remove the pairing and pending
bot requests without deleting recipes; restart to pair again. Protect database
backups because they include Telegram user identifiers and submitted links.
This is a personal single-user feature, not multi-user authorization.

## Privacy and limitations

Each conversion or retry requires consent. The app sends 12 video frames and any
public reel caption to OpenRouter and its selected vision provider. A separate,
optional consent sends audio to an audio-capable provider for a short,
potentially imperfect paraphrase of cooking-related speech. Existing queued
jobs do not acquire audio consent automatically. Do not submit private or
sensitive content without authorization. OpenRouter and its providers have their own data
retention policies. The app does not require Instagram credentials and does not
bypass private accounts, DRM or access controls. If retrieval fails, retry or
upload the video; you can optionally retain the original reel link with an
upload. Videos are limited to 100 MB and three minutes.
Local media also includes public caption metadata and raw model responses for
diagnosing failed conversions; protect backups accordingly.

The default vision model is `nvidia/nemotron-3.5-super-vl:free`; set
`OPENROUTER_MODEL` in `.env` to another explicitly free image-input OpenRouter
model. Optionally set `OPENROUTER_PROVIDER` to restrict routing to one OpenRouter
provider slug; an unavailable provider fails rather than falling back. The
optional audio model defaults to
`nvidia/nemotron-3-nano-omni-30b-a3b-reasoning:free`; configure it with
`OPENROUTER_AUDIO_MODEL` and optionally `OPENROUTER_AUDIO_PROVIDER`. Both models
must remain explicitly free and accept their respective inputs.
OpenRouter is the only supported API service. Before each request the application checks the current model catalog,
requires zero listed prices, and sets zero prompt, completion, image and
per-request provider price limits. There is no paid fallback. Availability,
quotas, model behavior and policies can change; see the
[model catalog](https://openrouter.ai/models) and
[OpenRouter limits](https://openrouter.ai/docs/api/reference/limits).
The documented price limits do not include a separate audio limit. Even a
zero-priced audio model may require an account balance: one test returned HTTP
402 requiring at least $0.50 in balance. The app does not enable billing or
switch to a paid model; visual analysis can continue with an explicit warning.
The result displays a reported request cost when the provider supplies one;
an absent cost is not treated as proof of zero cost. Invalid model output gets
at most one automatic repair attempt, which consumes another free-model request.
Audio analysis uses a separate request. If it is unavailable, the recipe
explicitly reports that limitation; missing cost data is never reported as zero.

When a public caption contains a recognizable numbered or headed cooking method, the
app preserves those steps instead of generating them from the video. The model
organizes ingredients and selects a cover; extracted ingredient names and
amounts must occur in the caption, but their pairing or completeness is not
guaranteed. Amounts without a recognizable nearby ingredient are omitted;
ratios and other complex formats may need review. The caption's instructions and quantities are not independently
verified for food safety. If the caption has no recognized numbered method,
the app analyzes video frames and optionally audio instead. Other caption
formats may not be recognized. Audio paraphrases may be misheard. In visual
recipes, amounts are estimates or unknown; an AI's claim
that something is visible is not treated as proof. Verify ingredients,
temperatures, doneness and food handling against the original and a trusted
source. Automated action and amount checks are deliberately conservative and
may reject otherwise valid output. A title/tag cross-check flags possible
missing ingredients for review without blocking a recipe; descriptive tags can
also trigger warnings, and the check cannot prove completeness. Editing is optional and saves with a version
check; completed recipes cannot be overwritten by retry. For video-generated recipes, numeric cooking times and temperatures are rejected
because they cannot be verified against the source by these checks. Caption
methods retain the creator's times and temperatures, which still require
independent food-safety verification. Model-guessed video timestamps are
omitted because sparse frames do not verify them. The app reads the linked
reel's public caption; it does not search for separately linked posts or comments.

## Backup and restore

Stop the API first and keep the database container running. Use PowerShell 7
and save both the SQL dump and `media` outside the repository in a private,
backed-up location:

```powershell
New-Item -ItemType Directory C:\EzCookingBackup | Out-Null
docker compose exec -T db pg_dump -U ezcooking -d ezcooking --clean --if-exists | Set-Content -Encoding utf8 C:\EzCookingBackup\recipes.sql
if ($LASTEXITCODE -ne 0) { throw 'Database backup failed.' }
Copy-Item .\media C:\EzCookingBackup\media -Recurse
```

Check the dump and media copies before relying on them. To restore, stop the API,
start the database container, **back up the current database and media first**,
and then replace the database contents and media with the matching backup:

```powershell
Get-Content -Raw C:\EzCookingBackup\recipes.sql | docker compose exec -T db psql -v ON_ERROR_STOP=1 -U ezcooking -d ezcooking
if ($LASTEXITCODE -ne 0) { throw 'Database restore failed.' }
```

Move the current `media` directory to a safe location, then copy the backed-up
`media` directory into the repository. Do not combine a database dump from one
backup with media from another. The restore SQL contains destructive `DROP`
statements; do not run it if you need to preserve the current database.

## Checks

```powershell
dotnet run --project .\EzCooking.RecipeValidation.Tests\EzCooking.RecipeValidation.Tests.csproj
.\test-integration.ps1
dotnet build .\EzCooking.Api\EzCooking.Api.csproj
npm run build --prefix .\EzCooking.Web
npm run lint --prefix .\EzCooking.Web
.\smoke.ps1
```

The smoke check requires a running API. Integration checks require Docker Desktop,
the local `postgres:17` image and restored .NET dependencies. The script starts
and removes a disposable PostgreSQL container on a separate loopback port; it
does not touch the application's database. The executable refuses the default
application port, requires explicit test credentials and a test database name,
and verifies the database's disposable-test marker. Never mark a database
containing data you need: the checks insert and remove data in it.
The API examples are in `EzCooking.Api\EzCooking.Api.http`.

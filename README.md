# EzCooking

EzCooking is a local, single-user recipe collection that turns public Instagram
reels or uploaded cooking videos into searchable recipes. Review generated
ingredients, quantities and instructions before cooking.

## Run on Windows

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0),
   [Node.js 24 LTS](https://nodejs.org/),
   [Docker Desktop](https://www.docker.com/products/docker-desktop/),
   [FFmpeg](https://ffmpeg.org/download.html) (including `ffmpeg` and `ffprobe`)
   and [yt-dlp](https://github.com/yt-dlp/yt-dlp#installation). Add the video
   tools to PATH, or set their executable paths in `.env` in the next step.
2. Open PowerShell in the project folder and create the configuration file:

   ```powershell
   if (-not (Test-Path .env)) { Copy-Item .env.example .env }
   notepad .env
   ```

   Set a long, unique `POSTGRES_PASSWORD` and an
   [OpenRouter API key](https://openrouter.ai/keys). Keep `.env` private.
   If needed, set `FFMPEG_PATH`, `FFPROBE_PATH` and `YTDLP_PATH` to the
   executable paths.
3. Install frontend dependencies:

   ```powershell
   npm install --prefix .\EzCooking.Web
   ```

4. Start Docker Desktop, wait until it is ready, and run:

   ```powershell
   .\start.ps1
   ```

5. Open **http://127.0.0.1:5187/**. Add a public reel link or upload a video
   you are entitled to use, then review and confirm the consent notice.
   Video frames and any public caption are sent to OpenRouter and its selected
   provider; audio is sent only with separate optional consent. Private or
   blocked reels cannot be downloaded; retry or upload a video instead.

For an optional Russian translation, open a saved recipe and select translation.
After your consent, its text is sent to OpenRouter and its selected provider in
one separate free-model request. The result is saved; translation can fail when
free quota is exhausted. Check translations against the original before cooking.
Set `OPENROUTER_TRANSLATION_MODEL` in `.env` to choose another explicitly free
text-capable model, or `OPENROUTER_TRANSLATION_PROVIDER` to restrict the provider.

Incomplete or uncertain drafts are marked **Needs review**. Check their warnings
and the original video before cooking or correcting them.

To stop, press Ctrl+C in the app window, then run `.\stop.ps1`. Your database
and local media are retained. For later use, start Docker Desktop and run
`.\start.ps1` again.

If startup fails, check that Docker Desktop is running, the video tools are
available, and ports 5187 and 55432 are free. Changing `POSTGRES_PASSWORD` in
`.env` does not change the password of an existing database volume.

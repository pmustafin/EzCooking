$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
docker compose up -d --wait
if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL failed to start.' }
npm run build --prefix .\EzCooking.Web
if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed. Run npm install --prefix .\EzCooking.Web first.' }
dotnet run --project .\EzCooking.Api\EzCooking.Api.csproj --no-launch-profile
if ($LASTEXITCODE -ne 0) { throw 'The application failed.' }

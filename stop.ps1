$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
docker compose stop
if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL did not stop cleanly.' }

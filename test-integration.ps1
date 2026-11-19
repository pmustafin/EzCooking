$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$testId = [guid]::NewGuid().ToString('N')
$containerName = "ezcooking-integration-$testId"
$output = Join-Path ([System.IO.Path]::GetTempPath()) "ezcooking-integration-$testId"
$database = 'ezcooking_test_disposable'
$password = [guid]::NewGuid().ToString('N')
$containerId = $null
$names = @('EZCOOKING_TEST_DATABASE', 'EZCOOKING_TEST_PASSWORD', 'EZCOOKING_TEST_PORT')
$previous = @{}
foreach ($name in $names) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

try {
    docker image inspect postgres:17 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'The postgres:17 image is not available locally. Install it before running these checks.'
    }

    dotnet build (Join-Path $root 'EzCooking.IntegrationChecks\EzCooking.IntegrationChecks.csproj') --no-restore --output $output
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not build integration checks. Restore the solution dependencies and retry.'
    }

    $containerId = docker run --rm -d --name $containerName `
        -e "POSTGRES_DB=$database" -e 'POSTGRES_USER=ezcooking' -e "POSTGRES_PASSWORD=$password" `
        -p '127.0.0.1::5432' postgres:17
    if ($LASTEXITCODE -ne 0 -or -not $containerId) {
        throw 'Could not start disposable PostgreSQL.'
    }

    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        docker exec $containerId pg_isready -U ezcooking -d $database 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) {
            $ready = $true
            break
        }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) {
        throw 'Disposable PostgreSQL did not become ready.'
    }

    docker exec $containerId psql -v ON_ERROR_STOP=1 -U ezcooking -d $database `
        -c "COMMENT ON DATABASE ezcooking_test_disposable IS 'ezcooking-disposable-integration-checks';"
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not mark the disposable test database.'
    }
    $binding = docker port $containerId 5432
    if ($LASTEXITCODE -ne 0 -or $binding -notmatch '^127\.0\.0\.1:(\d+)$') {
        throw 'Could not determine the disposable PostgreSQL loopback port.'
    }
    $env:EZCOOKING_TEST_DATABASE = $database
    $env:EZCOOKING_TEST_PASSWORD = $password
    $env:EZCOOKING_TEST_PORT = $Matches[1]
    dotnet (Join-Path $output 'EzCooking.IntegrationChecks.dll')
    if ($LASTEXITCODE -ne 0) {
        throw 'Integration checks failed.'
    }
}
finally {
    if ($containerId) {
        docker stop $containerId | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Could not stop disposable test container $containerId."
        }
    }
    foreach ($name in $names) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
    if (Test-Path -LiteralPath $output) {
        Remove-Item -LiteralPath $output -Recurse -Force
    }
}

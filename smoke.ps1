$ErrorActionPreference = 'Stop'
$base = 'http://127.0.0.1:5187'
function Check-Status($response, $expected, $message) {
    if ([int]$response.StatusCode -ne $expected) {
        throw "$message returned $($response.StatusCode), expected $expected."
    }
}
Check-Status (Invoke-WebRequest "$base/" -SkipHttpErrorCheck) 200 'Frontend'
Check-Status (Invoke-WebRequest "$base/openapi/v1.json" -SkipHttpErrorCheck) 200 'OpenAPI'
Check-Status (Invoke-WebRequest "$base/api/recipes" -SkipHttpErrorCheck) 200 'Recipe listing'
Check-Status (Invoke-WebRequest "$base/api/recipes" -Headers @{Origin='https://example.com'} -SkipHttpErrorCheck) 403 'Cross-origin request'
Check-Status (Invoke-WebRequest "$base/api/recipes" -Method Post -ContentType 'application/json' -Body '{"url":"https://example.com/reel/a","consent":true}' -SkipHttpErrorCheck) 400 'Invalid source'
Check-Status (Invoke-WebRequest "$base/api/recipes" -Method Post -ContentType 'application/json' -Body '{"url":"https://www.instagram.com/reel/a/","consent":false}' -SkipHttpErrorCheck) 400 'Missing consent'
$file = Get-Item (Join-Path $PSScriptRoot 'EzCooking.RecipeValidation.Tests\Program.cs')
Check-Status (Invoke-WebRequest "$base/api/recipes/upload" -Method Post -Form @{consent='false';video=$file} -SkipHttpErrorCheck) 400 'Upload without consent'
Write-Host 'API smoke checks passed.'

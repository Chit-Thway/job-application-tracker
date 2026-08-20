[CmdletBinding()]
param(
    [string]$ResourceGroup = "job-tracker-production",
    [string]$WebAppName = "chit-thway-job-tracker"
)

$ErrorActionPreference = "Stop"
$originalConnection = $env:ConnectionStrings__DefaultConnection

function Assert-LastCommandSucceeded([string]$Action) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Action failed with exit code $LASTEXITCODE."
    }
}

try {
    $repositoryRoot = Split-Path -Parent $PSScriptRoot
    Set-Location -LiteralPath $repositoryRoot

    $template = (Read-Host "Paste the production Supabase Session Pooler URI").Trim().Trim('"').Trim("'")
    if (-not ($template.Contains("[YOUR-PASSWORD]") -or $template.Contains("[YOURPASSWORD]"))) {
        throw "Use the Session Pooler URI that still contains the password placeholder."
    }

    $safeUriText = $template.Replace("[YOUR-PASSWORD]", "hidden").Replace("[YOURPASSWORD]", "hidden")
    $safeUri = [Uri]$safeUriText
    if ($safeUri.Scheme -notin @("postgres", "postgresql") -or
        -not $safeUri.Host.EndsWith(".supabase.com", [StringComparison]::OrdinalIgnoreCase)) {
        throw "The supplied value is not a Supabase PostgreSQL URI."
    }

    $userName = [Uri]::UnescapeDataString($safeUri.UserInfo.Split(':', 2)[0])
    if (-not $userName.StartsWith("postgres.", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Use the Session Pooler URI, whose username starts with 'postgres.'."
    }

    if ($userName.IndexOf("dxhmwxdllzfhoqoxtovk", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "STOP: this is the development project, not the production project."
    }

    $securePassword = Read-Host "Enter the production Supabase database password" -AsSecureString
    $plainPassword = [System.Net.NetworkCredential]::new("", $securePassword).Password
    $encodedPassword = [Uri]::EscapeDataString($plainPassword)
    $postgresUri = $template.Replace("[YOUR-PASSWORD]", $encodedPassword).Replace("[YOURPASSWORD]", $encodedPassword)

    $databaseName = $safeUri.AbsolutePath.Trim('/')
    if ([string]::IsNullOrWhiteSpace($databaseName)) {
        $databaseName = "postgres"
    }

    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection["Host"] = $safeUri.Host
    $connection["Port"] = $safeUri.Port
    $connection["Database"] = $databaseName
    $connection["Username"] = $userName
    $connection["Password"] = $plainPassword
    $connection["SSL Mode"] = "Require"
    $connection["Pooling"] = $true
    $connection["Maximum Pool Size"] = 10
    $npgsqlConnection = $connection.ConnectionString

    $env:ConnectionStrings__DefaultConnection = $npgsqlConnection

    Write-Host "Applying reviewed Entity Framework migrations..."
    dotnet ef database update `
        --project src\JobTracker.Web `
        --startup-project src\JobTracker.Web `
        --configuration Release
    Assert-LastCommandSucceeded "Entity Framework migration"

    $psql = (Get-Command psql -ErrorAction SilentlyContinue).Source
    if (-not $psql) {
        $knownPsql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
        if (Test-Path -LiteralPath $knownPsql) {
            $psql = $knownPsql
        }
        else {
            throw "psql was not found. Open a shell where PostgreSQL bin is on PATH."
        }
    }

    Write-Host "Configuring and verifying the hourly retention job..."
    & $psql `
        --dbname $postgresUri `
        --variable ON_ERROR_STOP=1 `
        --file database\supabase\configure-retention-cron.sql
    Assert-LastCommandSucceeded "Retention cron configuration"

    & $psql `
        --dbname $postgresUri `
        --variable ON_ERROR_STOP=1 `
        --file database\supabase\verify-retention-cron.sql
    Assert-LastCommandSucceeded "Retention cron verification"

    Write-Host "Saving the production connection as a protected App Service setting..."
    az webapp config appsettings set `
        --resource-group $ResourceGroup `
        --name $WebAppName `
        --settings "ConnectionStrings__DefaultConnection=$npgsqlConnection" `
        --output none
    Assert-LastCommandSucceeded "Azure App Service database configuration"

    Write-Host "Production database provisioning completed successfully."
}
finally {
    $env:ConnectionStrings__DefaultConnection = $originalConnection
    Remove-Variable securePassword, plainPassword, encodedPassword, postgresUri, connection, npgsqlConnection -ErrorAction SilentlyContinue
}

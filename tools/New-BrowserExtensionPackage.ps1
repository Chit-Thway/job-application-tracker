param(
    [uri] $DevelopmentTrackerOrigin,
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repo 'browser-extension'
$isDevelopment = $PSBoundParameters.ContainsKey('DevelopmentTrackerOrigin')
$packageName = if ($isDevelopment) { 'browser-extension-dev' } else { 'browser-extension' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo "artifacts/$packageName" }
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new package output directory.' }

if ($isDevelopment) {
    $uri = $DevelopmentTrackerOrigin
    if (!$uri.IsAbsoluteUri -or ($uri.Scheme -ne 'https' -and !($uri.Scheme -eq 'http' -and $uri.IsLoopback)) -or
        $uri.AbsolutePath -ne '/' -or $uri.Query -or $uri.Fragment -or $uri.UserInfo -or $uri.HostNameType -eq 'IPv6') {
        throw 'Use an HTTPS origin or a local HTTP origin, with an optional port.'
    }
}

$destination = (New-Item -ItemType Directory -Path $OutputDirectory).FullName
$files = @(
    'manifest.json', 'tracker-config.js', 'popup.html', 'popup.css', 'popup.js',
    'handoff.html', 'handoff.js', 'autofill-popup.js', 'autofill-connection.js',
    'autofill-matching.js', 'autofill-page.js', 'icons'
)
foreach ($file in $files) {
    Copy-Item -LiteralPath (Join-Path $source $file) -Destination $destination -Recurse
}

if ($isDevelopment) {
    $manifestPath = Join-Path $destination 'manifest.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $manifest.name += ' (Development)'
    $manifest.host_permissions = @("$($uri.Scheme)://$($uri.IdnHost)/*")
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
    $origin = $uri.GetLeftPart([UriPartial]::Authority) | ConvertTo-Json -Compress
    "const trackerOrigin = $origin;" | Set-Content -LiteralPath (Join-Path $destination 'tracker-config.js') -Encoding utf8NoBOM
}

Write-Output $destination

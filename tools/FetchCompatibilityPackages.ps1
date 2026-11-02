$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$projectRoot = Split-Path $PSScriptRoot -Parent
$feed = Join-Path $projectRoot '.tools\compat-feed'
$cache = Join-Path $projectRoot '.tools\packages'
New-Item -ItemType Directory -Force $feed | Out-Null
$queue = [System.Collections.Generic.Queue[string]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new()
$queue.Enqueue('microsoft.windowsappsdk.winui|1.8.260803003')
$queue.Enqueue('microsoft.windowsappsdk.runtime|1.8.260921001')
while ($queue.Count -gt 0) {
    $entry = $queue.Dequeue()
    if (-not $seen.Add($entry)) { continue }
    $id, $version = $entry.Split('|')
    if ($id -eq 'microsoft.windows.sdk.buildtools') { $version = '10.0.28000.2957' }
    $package = Join-Path $cache "$id\$version\$id.$version.nupkg"
    if (-not (Test-Path $package)) {
        $package = Join-Path $feed "$id.$version.nupkg"
        if (-not (Test-Path $package)) {
            Write-Output "Downloading $id $version"
            Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg" -OutFile $package -TimeoutSec 45
        }
    }
    $zip = [System.IO.Compression.ZipFile]::OpenRead($package)
    try {
        $manifest = $zip.Entries | Where-Object FullName -Like '*.nuspec' | Select-Object -First 1
        $reader = [System.IO.StreamReader]::new($manifest.Open())
        try { [xml]$xml = $reader.ReadToEnd() } finally { $reader.Dispose() }
        foreach ($dependency in $xml.SelectNodes('//*[local-name()="dependency"]')) {
            $dependencyId = $dependency.id.ToLowerInvariant()
            $minimum = $dependency.version.Trim('[',']','(',')').Split(',')[0].Trim()
            if ($dependencyId -notmatch '^[a-z0-9_.-]+$' -or $minimum -notmatch '^[0-9][a-zA-Z0-9.+-]*$') { throw "Unsupported dependency $dependencyId $minimum" }
            $queue.Enqueue("$dependencyId|$minimum")
        }
    } finally { $zip.Dispose() }
}
Write-Output "Prepared $($seen.Count) compatibility packages."

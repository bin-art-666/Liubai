$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$projectRoot = Split-Path $PSScriptRoot -Parent
$feed = Join-Path $projectRoot '.tools\feed'
New-Item -ItemType Directory -Force $feed | Out-Null
$queue = [Collections.Generic.Queue[string]]::new()
$seen = [Collections.Generic.HashSet[string]]::new()
function Get-OfficialPackage([string]$Url, [string]$Destination) {
    $head = Invoke-WebRequest $Url -Method Head -TimeoutSec 20
    $length = [long]($head.Headers['Content-Length'] | Select-Object -First 1)
    $hash = [string]($head.Headers['x-ms-meta-SHA512'] | Select-Object -First 1)
    if ($length -le 0) { throw "Missing package size: $Url" }
    $parts = $Destination + '.parts'
    New-Item -ItemType Directory -Force $parts | Out-Null
    $size = 4MB
    0..([int][Math]::Ceiling($length / $size) - 1) | ForEach-Object -Parallel {
        $start = [long]$_ * $using:size
        $end = [Math]::Min($using:length - 1, $start + $using:size - 1)
        $partPath = Join-Path $using:parts ('{0:D4}.part' -f $_)
        if ((Test-Path -LiteralPath $partPath) -and (Get-Item -LiteralPath $partPath).Length -eq ($end - $start + 1)) { return }
        & curl.exe -L --fail --retry 3 --connect-timeout 15 --max-time 60 --range "$start-$end" ($using:Url + '?part=' + $_) -o $partPath -sS
        if ($LASTEXITCODE -ne 0 -or (Get-Item -LiteralPath $partPath).Length -ne ($end - $start + 1)) { throw "Package part failed: $partPath" }
    } -ThrottleLimit 6
    $output = [IO.File]::Create($Destination + '.download')
    try {
        Get-ChildItem -LiteralPath $parts -Filter '*.part' | Sort-Object Name | ForEach-Object {
            $inputFile = [IO.File]::OpenRead($_.FullName)
            try { $inputFile.CopyTo($output) } finally { $inputFile.Dispose() }
        }
    } finally { $output.Dispose() }
    if ($hash) {
        $actual = [Convert]::ToBase64String([Convert]::FromHexString((Get-FileHash -LiteralPath ($Destination + '.download') -Algorithm SHA512).Hash))
        if ($actual -ne $hash) { throw "Package checksum mismatch: $Url" }
    }
    Move-Item -LiteralPath ($Destination + '.download') -Destination $Destination
}
@('microsoft.windowsappsdk.winui|2.6.4', 'microsoft.windowsappsdk.runtime|2.6.4', 'microsoft.windows.sdk.buildtools|10.0.28000.2957', 'microsoft.netcore.app.runtime.win-x64|10.0.14', 'microsoft.windows.sdk.net.ref|10.0.26100.57', 'microsoft.windowsdesktop.app.runtime.win-x64|10.0.14', 'microsoft.aspnetcore.app.runtime.win-x64|10.0.14') | ForEach-Object { $queue.Enqueue($_) }
while ($queue.Count -gt 0) {
    $entry = $queue.Dequeue()
    if (-not $seen.Add($entry)) { continue }
    $id, $version = $entry.Split('|')
    $package = Join-Path $feed "$id.$version.nupkg"
    if (-not (Test-Path -LiteralPath $package)) {
        Write-Output "Downloading $id $version"
        Get-OfficialPackage "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg" $package
    }
    $zip = [IO.Compression.ZipFile]::OpenRead($package)
    try {
        $manifest = $zip.Entries | Where-Object FullName -Like '*.nuspec' | Select-Object -First 1
        $reader = [IO.StreamReader]::new($manifest.Open())
        try { [xml]$xml = $reader.ReadToEnd() } finally { $reader.Dispose() }
        foreach ($dependency in $xml.SelectNodes('//*[local-name()="dependency"]')) {
            $dependencyId = $dependency.id.ToLowerInvariant()
            $minimum = $dependency.version.Trim('[',']','(',')').Split(',')[0].Trim()
            if ($dependencyId -notmatch '^[a-z0-9_.-]+$' -or $minimum -notmatch '^[0-9][a-zA-Z0-9.+-]*$') { throw "Unsupported dependency $dependencyId $minimum" }
            if ($dependencyId -eq 'microsoft.windows.sdk.buildtools') { $minimum = '10.0.28000.2957' }
            $queue.Enqueue("$dependencyId|$minimum")
        }
    } finally { $zip.Dispose() }
}
Write-Output "Prepared $($seen.Count) packages in $feed"

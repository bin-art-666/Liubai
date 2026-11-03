param([switch]$Run, [switch]$Offline)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath($PSScriptRoot)
$output = Join-Path $workspace 'dist'
$staging = Join-Path $workspace '.build\publish'
$backup = Join-Path $workspace '.build\rollback'

function Assert-WorkspaceDirectory([string]$Path) {
    $absolute = [IO.Path]::GetFullPath($Path)
    if (-not $absolute.StartsWith($workspace + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Path is outside the workspace.' }
    if (Test-Path -LiteralPath $absolute) {
        $item = Get-Item -LiteralPath $absolute -Force
        if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Unexpected directory: $absolute" }
        if ((Resolve-Path -LiteralPath $absolute).Path -ne $absolute) { throw 'Unexpected resolved directory.' }
    }
}

function Remove-GeneratedDirectory([string]$Path) {
    Assert-WorkspaceDirectory $Path
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force }
}

# Keep the same executable path for startup and shortcuts. Never publish over a running copy.
$running = Get-Process Liubai -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $output 'Liubai.exe') }
if ($running) { throw 'Exit Liubai from its tray menu before building. Use dist\Liubai.exe as the only runtime.' }
Assert-WorkspaceDirectory $output
Assert-WorkspaceDirectory $staging
Assert-WorkspaceDirectory $backup
if ((Test-Path -LiteralPath $backup) -and -not (Test-Path -LiteralPath $output)) {
    Move-Item -LiteralPath $backup -Destination $output
}
Remove-GeneratedDirectory $staging
Remove-GeneratedDirectory $backup
$dotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) {
    $installed = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $installed) { throw 'Install .NET 10 SDK, or put a portable SDK in .tools\dotnet.' }
    $dotnet = $installed.Source
}
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$project = Join-Path $PSScriptRoot 'src\Liubai\Liubai.csproj'
$restoreArguments = if ($Offline) { @('--source', (Join-Path $PSScriptRoot '.tools\feed')) } else { @() }
& $dotnet publish $project -c Release -p:Platform=x64 -o $staging --verbosity minimal @restoreArguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
foreach ($file in @('Liubai.exe', 'Liubai.dll', 'resources.pri')) {
    if (-not (Test-Path -LiteralPath (Join-Path $staging $file))) { throw "Publish output is incomplete: $file" }
}
try {
    if (Test-Path -LiteralPath $output) { Move-Item -LiteralPath $output -Destination $backup }
    Move-Item -LiteralPath $staging -Destination $output
} catch {
    if (-not (Test-Path -LiteralPath $output) -and (Test-Path -LiteralPath $backup)) {
        Move-Item -LiteralPath $backup -Destination $output
    }
    throw
}
# The rollback copy is temporary. Successful builds leave only the formal runtime in dist.
Remove-GeneratedDirectory $backup
foreach ($relative in @('src\Liubai\bin', 'src\Liubai\obj')) {
    Remove-GeneratedDirectory (Join-Path $workspace $relative)
}
Write-Output "Built: $output\Liubai.exe"
if ($Run) { Start-Process -FilePath (Join-Path $output 'Liubai.exe') -WindowStyle Hidden }

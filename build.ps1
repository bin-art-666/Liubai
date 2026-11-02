$ErrorActionPreference = 'Stop'
$dotnet = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) {
    $installed = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $installed) { throw 'Install .NET 10 SDK or place the portable SDK in .tools\dotnet.' }
    $dotnet = $installed.Source
}
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$project = Join-Path $PSScriptRoot 'src\Liubai.WinUI\Liubai.WinUI.csproj'
if (-not (Test-Path (Join-Path $PSScriptRoot 'src\Liubai.WinUI\obj\project.assets.json'))) {
    & $dotnet restore $project -p:Platform=x64 --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
}
$output = Join-Path $PSScriptRoot 'dist'
$stagingOutput = Join-Path $PSScriptRoot ('.build\publish-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stagingOutput | Out-Null
& $dotnet publish $project -c Release -p:Platform=x64 --no-restore -o $stagingOutput --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if (Test-Path $output) {
    $resolvedOutput = (Resolve-Path $output).Path
    $expectedOutput = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'dist'))
    if ($resolvedOutput -ne $expectedOutput -or ((Get-Item -LiteralPath $resolvedOutput).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Unexpected publish directory.' }
    $previousOutput = Join-Path $PSScriptRoot ('.build\dist-backup-' + [Guid]::NewGuid().ToString('N'))
    if (-not ([IO.Path]::GetFullPath($previousOutput).StartsWith([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '.build')) + '\', [StringComparison]::OrdinalIgnoreCase))) { throw 'Unexpected backup directory.' }
    Move-Item -LiteralPath $resolvedOutput -Destination $previousOutput
}
Move-Item -LiteralPath $stagingOutput -Destination $output
Write-Output "Built: $output\Liubai.exe"

param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string] $Version,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string] $Sha256,
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string] $Repository = 'rob1nzon/Clippy',
    [string] $ManifestPath = (Join-Path $PSScriptRoot '../bucket/clippy.json')
)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -AsHashtable
if ([version]$Version -lt [version]$manifest.version) { throw 'Refusing to downgrade the Scoop manifest.' }
$manifest.version = $Version
$manifest.homepage = "https://github.com/$Repository"
$manifest.architecture.'64bit'.url = "https://github.com/$Repository/releases/download/v$Version/Clippy-win-x64.zip"
$manifest.architecture.'64bit'.hash = $Sha256.ToLowerInvariant()
$manifest.autoupdate.architecture.'64bit'.url = "https://github.com/$Repository/releases/download/v`$version/Clippy-win-x64.zip"
$json = ($manifest | ConvertTo-Json -Depth 10) + "`n"
[IO.File]::WriteAllText((Resolve-Path -LiteralPath $ManifestPath).Path, $json, [Text.UTF8Encoding]::new($false))

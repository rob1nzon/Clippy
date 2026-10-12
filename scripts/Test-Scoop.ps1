param(
    [Parameter(Mandatory)][string] $ScoopRoot,
    [string] $ManifestPath = (Join-Path $PSScriptRoot '../bucket/clippy.json')
)
$ErrorActionPreference = 'Stop'
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if (!(Get-Content -LiteralPath $ManifestPath -Raw | Test-Json -SchemaFile "$ScoopRoot/schema.json")) { throw 'Scoop schema validation failed.' }
if ($manifest.architecture.'64bit'.hash -notmatch '^[a-f0-9]{64}$') { throw 'Missing SHA256.' }
if ($manifest.extract_dir -ne 'Clippy-win-x64') { throw 'Unexpected archive root.' }

# All generated repos and Scoop installations are isolated to this CI run.
$testRoot = Join-Path $env:RUNNER_TEMP ('clippy-scoop-test-' + [guid]::NewGuid())
$bucketSource = Join-Path $testRoot 'bucket-source'
New-Item -ItemType Directory -Path "$bucketSource/bucket" -Force | Out-Null
$fixture = Join-Path $bucketSource 'bucket/clippy.json'
Copy-Item -LiteralPath $ManifestPath -Destination $fixture

# Check updater output and reject bad input without changing the fixture.
$updater = Join-Path $PSScriptRoot 'Update-ScoopManifest.ps1'
& $updater -Version '999.0.0' -Sha256 ('a' * 64) -ManifestPath $fixture
$updated = Get-Content -LiteralPath $fixture -Raw | ConvertFrom-Json
if ($updated.version -ne '999.0.0' -or $updated.architecture.'64bit'.hash -ne ('a' * 64) -or
    $updated.architecture.'64bit'.url -notmatch '/v999\.0\.0/Clippy-win-x64.zip$' -or
    $updated.autoupdate.architecture.'64bit'.url -notmatch 'v\$version/') { throw 'Manifest updater failed.' }
$before = (Get-FileHash -LiteralPath $fixture).Hash
$rejected = $false
try { & $updater -Version '0.1.0' -Sha256 ('a' * 64) -ManifestPath $fixture } catch { $rejected = $true }
if (!$rejected -or (Get-FileHash -LiteralPath $fixture).Hash -ne $before) { throw 'Updater allowed downgrade.' }
$rejected = $false
try { & $updater -Version '999.0.1' -Sha256 'invalid' -ManifestPath $fixture } catch { $rejected = $true }
if (!$rejected -or (Get-FileHash -LiteralPath $fixture).Hash -ne $before) { throw 'Updater allowed invalid hash.' }
Copy-Item -LiteralPath $ManifestPath -Destination $fixture -Force

# Install the previous real release, then upgrade through Scoop to this manifest.
$previous = Get-Content -LiteralPath $fixture -Raw | ConvertFrom-Json -AsHashtable
$previous.version = '0.5.0'
$previous.architecture.'64bit'.url = 'https://github.com/rob1nzon/Clippy/releases/download/v0.5.0/Clippy-win-x64.zip'
$previous.architecture.'64bit'.hash = '5dfe0125788ee53099344b71c32ebdd5d0ddee7852632ef2d6c02bc3d7da338a'
$previous | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $fixture -Encoding utf8NoBOM
git -C $bucketSource init -b master
git -C $bucketSource config user.name 'Scoop test'
git -C $bucketSource config user.email 'scoop-test@example.invalid'
git -C $bucketSource add bucket/clippy.json
git -C $bucketSource commit -m 'test: previous release'
if ($LASTEXITCODE -ne 0) { throw 'Could not initialize test bucket.' }

$env:SCOOP = Join-Path $testRoot 'scoop-home'
$env:SCOOP_GLOBAL = Join-Path $testRoot 'scoop-global'
$scoopCurrent = Join-Path $env:SCOOP 'apps/scoop/current'
New-Item -ItemType Directory -Path $scoopCurrent -Force | Out-Null
New-Item -ItemType Directory -Path "$env:SCOOP/shims", "$env:SCOOP/buckets", "$env:SCOOP/cache" -Force | Out-Null
Get-ChildItem -LiteralPath $ScoopRoot -Force | Copy-Item -Destination $scoopCurrent -Recurse -Force
'{}' | Set-Content -LiteralPath (Join-Path $env:SCOOP 'config.json')
$scoop = Join-Path $scoopCurrent 'bin/scoop.ps1'
& $scoop bucket add clippy-test $bucketSource
if ($LASTEXITCODE -ne 0) { throw 'Bucket add failed.' }
& $scoop install clippy-test/clippy
if ($LASTEXITCODE -ne 0) { throw 'Scoop install failed.' }
$appDir = Join-Path $env:SCOOP 'apps/clippy/current'
if (!(Test-Path "$appDir/Clippy.exe") -or !(Test-Path "$appDir/Clippy.Core.dll")) { throw 'Portable files missing.' }
if (!(Test-Path "$env:SCOOP/shims/Clippy.exe")) { throw 'Executable shim missing.' }

# An existing settings file must survive install/update/uninstall outside app dirs.
$settingsPath = Join-Path $env:LOCALAPPDATA 'Clippy/settings.json'
if (Test-Path -LiteralPath $settingsPath) { throw 'Expected clean CI profile; refusing to overwrite settings.' }
New-Item -ItemType Directory -Path (Split-Path $settingsPath) -Force | Out-Null
'{"ServerUrl":"http://192.168.1.20:8080/v1","VoiceInputEnabled":true}' | Set-Content -LiteralPath $settingsPath
$settingsHash = (Get-FileHash -LiteralPath $settingsPath).Hash
Copy-Item -LiteralPath $ManifestPath -Destination $fixture -Force
git -C $bucketSource add bucket/clippy.json
git -C $bucketSource commit -m 'test: current release'
if ($LASTEXITCODE -ne 0) { throw 'Could not update test bucket.' }
& $scoop update clippy
if ($LASTEXITCODE -ne 0) { throw 'Scoop update failed.' }
$installed = Get-Content "$appDir/manifest.json" -Raw | ConvertFrom-Json
if ($installed.version -ne $manifest.version) { throw 'Scoop did not upgrade to the current version.' }
if ((Get-FileHash -LiteralPath $settingsPath).Hash -ne $settingsHash) { throw 'Settings changed during upgrade.' }
& $scoop uninstall clippy
if ($LASTEXITCODE -ne 0) { throw 'Scoop uninstall failed.' }
if (!(Test-Path -LiteralPath $settingsPath) -or (Get-FileHash -LiteralPath $settingsPath).Hash -ne $settingsHash) { throw 'Scoop removed user settings.' }
Write-Host 'PASS: schema, updater guards, real install, shim, upgrade and settings preservation.'

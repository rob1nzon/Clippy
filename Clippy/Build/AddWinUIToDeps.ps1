param(
    [Parameter(Mandatory = $true)]
    [string]$DepsPath
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $DepsPath)) {
    throw "Dependency file not found: $DepsPath"
}

$deps = Get-Content -LiteralPath $DepsPath -Raw | ConvertFrom-Json
$targetName = $deps.runtimeTarget.name
$target = $deps.targets.$targetName
$project = $target.PSObject.Properties |
    Where-Object { $_.Name -like "Clippy/*" } |
    Select-Object -First 1

if (-not $project) {
    throw "Clippy project entry not found in $DepsPath"
}

if (-not $project.Value.runtime) {
    $project.Value | Add-Member -MemberType NoteProperty -Name runtime -Value ([pscustomobject]@{})
}

$project.Value.runtime | Add-Member `
    -MemberType NoteProperty `
    -Name "Microsoft.WinUI.dll" `
    -Value ([pscustomobject]@{ assemblyVersion = "3.0.0.0" }) `
    -Force

$json = $deps | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($DepsPath, $json, [System.Text.UTF8Encoding]::new($false))

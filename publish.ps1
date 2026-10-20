<#
  Publishes a new version of Eric's Home Theater to GitHub Releases, and keeps the
  repository's front page (README.md) up to date. Installed and local copies find the new
  version within 6 hours (or right away with Settings > Check for updates), download it,
  check it, and update themselves.

  Usage:   .\publish.ps1 1.5.0 "What changed in this version"
           .\publish.ps1 -ReadmeOnly          (just update the README on GitHub)

  One-time setup:
    1. Put your repository (owner/name) in update_repo.txt
    2. Install the GitHub CLI:   winget install GitHub.cli
    3. Sign in:                  gh auth login
#>
param(
    [string]$Version,
    [string]$Notes = "",
    [switch]$ReadmeOnly
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (-not $ReadmeOnly -and $Version -notmatch '^\d+\.\d+\.\d+$') { throw "Use a version number like 1.5.0 (or -ReadmeOnly)" }

$repo = Get-Content update_repo.txt | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') } | Select-Object -First 1
if (-not $repo -or $repo -notmatch '^[\w.-]+/[\w.-]+$') { throw "Put your GitHub repository (owner/name) in update_repo.txt first." }

$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
if (-not $gh -and (Test-Path "$env:ProgramFiles\GitHub CLI\gh.exe")) { $gh = "$env:ProgramFiles\GitHub CLI\gh.exe" }
if (-not $gh) { throw "Install the GitHub CLI first (winget install GitHub.cli), then run: gh auth login" }
& $gh auth status *> $null
if ($LASTEXITCODE -ne 0) { throw "Sign in to GitHub first: gh auth login" }

function Ensure-Repo {
    # The repository must be public so the app can download updates without a password.
    $visibility = & $gh repo view $repo --json visibility --jq .visibility 2>$null
    if ($LASTEXITCODE -ne 0) {
        & $gh repo create $repo --public --add-readme --description "Eric's Home Theater - your movie collection, TV-style"
        if ($LASTEXITCODE -ne 0) { throw "Couldn't create the repository $repo." }
    }
    elseif ($visibility -ne "PUBLIC") {
        throw "$repo is private, so installed copies couldn't download updates. Make it public on GitHub (Settings > General > Change visibility), then publish again."
    }
}

function Publish-Readme([string]$message) {
    $readme = Join-Path $PSScriptRoot "README.md"
    if (-not (Test-Path $readme)) { return }
    $content = [Convert]::ToBase64String([IO.File]::ReadAllBytes($readme))
    $sha = & $gh api "repos/$repo/contents/README.md" --jq .sha 2>$null
    $hadOne = $LASTEXITCODE -eq 0 -and $sha
    $ghArgs = @("api", "repos/$repo/contents/README.md", "-X", "PUT", "-f", "message=$message", "-f", "content=$content")
    if ($hadOne) { $ghArgs += @("-f", "sha=$sha") }
    & $gh @ghArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Warning "Couldn't update README.md on GitHub." } else { Write-Host "README.md updated on GitHub." }
}

if ($ReadmeOnly) {
    Ensure-Repo
    Publish-Readme "Update README"
    Write-Host "https://github.com/$repo"
    return
}

$main = Join-Path $PSScriptRoot "src\Main.cs"
$setup = Join-Path $PSScriptRoot "installer\Setup.cs"
$current = [regex]::Match([IO.File]::ReadAllText($main), 'AssemblyFileVersion\("(\d+\.\d+\.\d+)').Groups[1].Value
if ([version]$Version -le [version]$current) { throw "Version $Version must be newer than the current version $current." }

# Stamp the new version into the app and the installer (installed copies compare these numbers).
foreach ($file in $main, $setup) {
    $t = [IO.File]::ReadAllText($file)
    $t = [regex]::Replace($t, '(Assembly(?:File)?Version\(")\d+\.\d+\.\d+(?:\.\d+)?', ('${1}' + $Version + '.0'))
    $t = [regex]::Replace($t, '(public const string Version = ")[^"]+', ('${1}' + $Version))
    [IO.File]::WriteAllText($file, $t, (New-Object Text.UTF8Encoding($true)))
}

cmd /c "`"$PSScriptRoot\build_installer.cmd`""
if ($LASTEXITCODE -ne 0) { throw "The build failed; nothing was published." }

$exe = Join-Path $PSScriptRoot "dist\EricsHomeTheater-Setup.exe"
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
Set-Content "$exe.sha256" "$hash  EricsHomeTheater-Setup.exe" -Encoding ASCII

Ensure-Repo
Publish-Readme "Update README for $Version"

if (-not $Notes) { $Notes = "Eric's Home Theater $Version" }
& $gh release create "v$Version" $exe "$exe.sha256" --repo $repo --title "Eric's Home Theater $Version" --notes $Notes
if ($LASTEXITCODE -ne 0) { throw "Publishing to GitHub failed." }

Write-Host ""
Write-Host "Published version $Version to https://github.com/$repo/releases"
Write-Host "Copies of the app update within 6 hours, or right away with Settings > Check for updates."

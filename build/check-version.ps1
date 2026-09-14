#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Fails when a documented version string disagrees with the package version.

.DESCRIPTION
    The <Version> element in DynamicWhere.ex.csproj is the single source of truth. The same
    number is repeated in the README, the packaged reference, and the docs site, and drift
    there publishes a package whose own documentation points at a different release.

    Every reference is matched by a pattern below. A captured version that differs from the
    csproj fails the build, and so does a pattern that matches nothing, so a reference deleted or
    reworded by accident is not silently accepted.

    Run it from anywhere:  pwsh ./build/check-version.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root 'DynamicWhere.ex/DynamicWhere.ex.csproj'

$match = [regex]::Match((Get-Content $csproj -Raw), '<Version>([^<]+)</Version>')

if (-not $match.Success) {
    Write-Host "::error::No <Version> element found in $csproj"
    exit 1
}

$expected = $match.Groups[1].Value.Trim()

Write-Host "Package version: $expected"

# Each file maps to the patterns that carry a version, capturing it in group 1.
# Versions are matched as digits rather than as non-whitespace so a template literal's
# closing backtick or a JSX tag is never swallowed into the capture.
$semver = '\d+\.\d+\.\d+[\w.-]*'

$targets = [ordered]@{
    'README.md' = @(
        "DynamicWhere\.ex --version ($semver)",
        "DynamicWhere\.ex -Version ($semver)"
    )
    'DynamicWhere.ex/DOC.md' = @(
        "\*\*Version:\*\* ($semver)",
        "DynamicWhere\.ex --version ($semver)"
    )
    # What nuget.org shows under Release Notes. Each release adds an entry at the top headed with
    # its version, and nothing else ties that heading to <Version>: a bump that forgot the entry
    # would publish a package whose release notes open by describing the release before it.
    'DynamicWhere.ex/DynamicWhere.ex.csproj' = @(
        "<PackageReleaseNotes>v($semver)"
    )
    'OfficialWebsite/lib/nav.ts' = @(
        '\bversion: "([^"]+)"'
    )
    'OfficialWebsite/package.json' = @(
        '"version": "([^"]+)"'
    )
    'OfficialWebsite/app/docs/installation/page.tsx' = @(
        "DynamicWhere\.ex --version ($semver)",
        "DynamicWhere\.ex -Version ($semver)",
        'Include="DynamicWhere\.ex" Version="([^"]+)"'
    )
    'OfficialWebsite/app/docs/page.tsx' = @(
        'Version <strong>([^<]+)</strong>',
        "DynamicWhere\.ex --version ($semver)"
    )
    # The three companion packages ship on their own and version in lockstep with the core. They
    # are listed here because publish.yml packs all four from one push: a bump that missed one
    # would ship a companion declaring a dependency on a core version it was never built or
    # tested against, and nothing else in the build would notice. Their release notes are
    # checked for the same reason as the core's.
    'DynamicWhere.ex.Policies.Redis/DynamicWhere.ex.Policies.Redis.csproj' = @(
        '<Version>([^<]+)</Version>',
        "<PackageReleaseNotes>v($semver)"
    )
    'DynamicWhere.ex.Policies.EntityFrameworkCore/DynamicWhere.ex.Policies.EntityFrameworkCore.csproj' = @(
        '<Version>([^<]+)</Version>',
        "<PackageReleaseNotes>v($semver)"
    )
    'DynamicWhere.ex.Policies.AspNetCore/DynamicWhere.ex.Policies.AspNetCore.csproj' = @(
        '<Version>([^<]+)</Version>',
        "<PackageReleaseNotes>v($semver)"
    )
    # The install command for each companion package, on the docs pages that teach it. The core's
    # own pattern above cannot match these: 'DynamicWhere\.ex --version' does not match
    # 'DynamicWhere.ex.Policies.Redis --version', so all three sat outside the guard and a bump
    # would have left the site telling people to install the version before it.
    'OfficialWebsite/app/docs/policies/providers/page.tsx' = @(
        "DynamicWhere\.ex\.Policies\.Redis --version ($semver)",
        "DynamicWhere\.ex\.Policies\.EntityFrameworkCore --version ($semver)"
    )
    'OfficialWebsite/app/docs/policies/admin/page.tsx' = @(
        "DynamicWhere\.ex\.Policies\.AspNetCore --version ($semver)"
    )
    # The agent reference states the version twice and lists the install command. It duplicates the
    # docs by design, which is exactly why it needs the guard: nothing else would notice it going
    # stale, and an agent reading a stale version writes against an API that shipped before it.
    'OfficialWebsite/public/llms.txt' = @(
        "Version ($semver) . targets net6\.0",
        "DynamicWhere\.ex --version ($semver)"
    )
    # The page that serves that reference says which version it was generated against, in prose
    # none of the patterns above reach.
    'OfficialWebsite/app/docs/ai/page.tsx' = @(
        "generated against version ($semver)"
    )
}

$problems = @()

foreach ($target in $targets.GetEnumerator()) {
    $path = Join-Path $root $target.Key

    if (-not (Test-Path $path)) {
        $problems += "$($target.Key): file not found"
        continue
    }

    $text = Get-Content $path -Raw
    $found = 0
    $unmatched = 0

    foreach ($pattern in $target.Value) {
        $hits = [regex]::Matches($text, $pattern)

        # Every pattern has to match, not merely one per file. In a file with two patterns, the one
        # still matching would keep the file looking guarded while a reference reworded out of the
        # other's reach went stale unread.
        if ($hits.Count -eq 0) {
            $problems += "$($target.Key): no reference matches $pattern"
            $unmatched++
        }

        foreach ($hit in $hits) {
            $found++
            $actual = $hit.Groups[1].Value

            if ($actual -ne $expected) {
                $problems += "$($target.Key): found '$actual', expected '$expected'"
            }
        }
    }

    if ($unmatched -eq 0) {
        Write-Host "  ok  $($target.Key) ($found reference$(if ($found -ne 1) { 's' }))"
    }
}

if ($problems.Count -gt 0) {
    Write-Host "::error::Documented versions do not match the package version '$expected'"

    foreach ($problem in $problems) {
        Write-Host "  $problem"
    }

    Write-Host ''
    Write-Host "Bump every reference to '$expected', or correct <Version> in the csproj."
    exit 1
}

Write-Host "All version references match $expected."

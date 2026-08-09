#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Fails when a documented version string disagrees with the package version.

.DESCRIPTION
    The <Version> element in DynamicWhere.ex.csproj is the single source of truth. The same
    number is repeated in the README, the packaged reference, and the docs site, and drift
    there publishes a package whose own documentation points at a different release.

    Every reference is matched by a pattern below. A captured version that differs from the
    csproj fails the build, and a file that yields no match at all fails too, so a reference
    deleted by accident is not silently accepted.

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

    foreach ($pattern in $target.Value) {
        foreach ($hit in [regex]::Matches($text, $pattern)) {
            $found++
            $actual = $hit.Groups[1].Value

            if ($actual -ne $expected) {
                $problems += "$($target.Key): found '$actual', expected '$expected'"
            }
        }
    }

    if ($found -eq 0) {
        $problems += "$($target.Key): no version reference found"
    }
    else {
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

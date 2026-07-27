set windows-shell := ["pwsh.exe", "-NoLogo", "-Command"]

solution      := "TemporalAspire.slnx"
configuration := "Release"
artifacts_dir := "artifacts/packages"
tests_dir     := "tests/TemporalCommunity.Aspire.Hosting.Tests"
package_dir   := "src/TemporalCommunity.Aspire.Hosting"
package_id    := "TemporalCommunity.Aspire.Hosting"
# Runs minver (local tool — .config/dotnet-tools.json) to compute the current version from git tags.
# The sed/tr reads MinVerDefaultPreReleaseIdentifiers from Directory.Build.props so the pre-release
# label has a single source of truth; minver-cli must be installed via `dotnet tool restore`.
prerelease    := `sed -n 's/.*<MinVerDefaultPreReleaseIdentifiers>\(.*\)<\/MinVerDefaultPreReleaseIdentifiers>.*/\1/p' Directory.Build.props | tr -d ' '`
version       := `dotnet tool run minver --tag-prefix "" --default-pre-release-identifiers $(sed -n 's/.*<MinVerDefaultPreReleaseIdentifiers>\(.*\)<\/MinVerDefaultPreReleaseIdentifiers>.*/\1/p' Directory.Build.props | tr -d ' ')`

default:
    @just --list

info:
    @echo "Solution:      {{solution}}"
    @echo "Package:       {{package_id}}"
    @echo "Version:       {{version}}"
    @echo "Prerelease:    {{prerelease}}"
    @echo "Configuration: {{configuration}}"
    @echo "Artifacts:     {{artifacts_dir}}"

restore:
    dotnet restore "{{solution}}"

build: restore
    dotnet build "{{solution}}" --configuration "{{configuration}}" --no-restore --nologo

test: build
    dotnet test "{{tests_dir}}" --configuration "{{configuration}}" --no-build --nologo --logger "trx;LogFileName=unit.trx"

integration-test: build
    RUN_TEMPORAL_INTEGRATION_TESTS=1 dotnet test "{{tests_dir}}" --configuration "{{configuration}}" --no-build --nologo --filter "FullyQualifiedName~TemporalDevServerIntegrationTests"

pack: build
    mkdir -p "{{artifacts_dir}}"
    dotnet pack "{{package_dir}}" --configuration "{{configuration}}" --no-build --nologo --output "{{artifacts_dir}}"

[unix]
pack-verify: pack
    #!/usr/bin/env bash
    set -euo pipefail
    pkg=$(ls "{{artifacts_dir}}"/{{package_id}}.{{version}}.nupkg 2>/dev/null | head -1)
    [ -n "$pkg" ] || { echo "ERROR: no .nupkg found in {{artifacts_dir}}"; exit 1; }
    echo "==> Checking lib/ folders in $(basename "$pkg")"
    for tfm in net10.0 net9.0 net8.0; do
        if unzip -Z1 "$pkg" | grep -Fx "lib/$tfm/{{package_id}}.dll" >/dev/null; then
            echo "  ✓ lib/$tfm/ present"
        else
            echo "  ✗ ERROR: lib/$tfm/ missing from nupkg" >&2
            exit 1
        fi
    done
    if unzip -Z1 "$pkg" | grep -Fx "icon.png" >/dev/null; then
        echo "  ✓ icon.png present"
    else
        echo "  ✗ ERROR: icon.png missing from nupkg" >&2
        exit 1
    fi

[windows]
pack-verify: pack
    $pkg = Get-ChildItem "{{artifacts_dir}}/{{package_id}}.{{version}}.nupkg" | Select-Object -First 1
    if (-not $pkg) { throw "ERROR: no .nupkg found in {{artifacts_dir}}" }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($pkg.FullName)
    try {
      foreach ($tfm in @("net10.0", "net9.0", "net8.0")) {
        $entry = "lib/$tfm/{{package_id}}.dll"
        if (-not ($zip.Entries | Where-Object FullName -eq $entry)) { throw "ERROR: $entry missing from nupkg" }
        Write-Host "  ✓ lib/$tfm/ present"
      }
      if (-not ($zip.Entries | Where-Object FullName -eq "icon.png")) { throw "ERROR: icon.png missing from nupkg" }
      Write-Host "  ✓ icon.png present"
    } finally {
      $zip.Dispose()
    }

publish-nuget: pack
    dotnet nuget push "{{artifacts_dir}}/*.nupkg" \
        --source "https://api.nuget.org/v3/index.json" \
        --api-key "$NUGET_API_KEY" \
        --skip-duplicate

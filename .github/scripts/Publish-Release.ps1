param(
    [string]$ReleaseTag = '',
    [string]$OutputDirectory = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Push-Location $repositoryRoot
try {
    # Use a fresh staging directory so older multi-file publishes cannot leak into the ZIPs.
    $publishRoot = Join-Path $OutputDirectory "publish/$([Guid]::NewGuid().ToString('N'))"
    $releaseRoot = Join-Path $OutputDirectory 'releases'
    New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
    foreach ($module in @(@{ Name = 'App'; Folder = 'app' }, @{ Name = 'Cli'; Folder = 'cli' }, @{ Name = 'Mcp'; Folder = 'mcp' })) {
        $project = "src/MissionEditor.$($module.Name)/MissionEditor.$($module.Name).csproj"
        $destination = Join-Path $publishRoot $module.Folder
        & dotnet publish $project --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:GenerateDocumentationFile=false --output $destination
        if ($LASTEXITCODE -ne 0) { throw "Publish failed: $project" }
        $framework = if ($module.Name -eq 'App') { 'net10.0-windows' } else { 'net10.0' }
        $buildDirectory = "src/MissionEditor.$($module.Name)/bin/Release/$framework/win-x64"
        $executableVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path (Join-Path $destination "MissionEditor.$($module.Name).exe"))).ProductVersion
        $coreVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path (Join-Path $buildDirectory 'MissionEditor.Core.dll'))).ProductVersion
        if ($coreVersion -ne $executableVersion) { throw "Core version mismatch in $project." }
        $unexpectedFiles = @(Get-ChildItem $destination -File -Recurse | Where-Object Name -ne "MissionEditor.$($module.Name).exe")
        if ($unexpectedFiles.Count -gt 0) { throw "Unexpected files in single-file publish: $($unexpectedFiles.FullName -join ', ')" }
    }
    $appDll = Join-Path $publishRoot 'app/MissionEditor.App.exe'
    $fullVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path $appDll)).ProductVersion
    $version = $fullVersion.Split('+')[0]
    foreach ($moduleDll in @('cli/MissionEditor.Cli.exe', 'mcp/MissionEditor.Mcp.exe')) {
        $moduleVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path (Join-Path $publishRoot $moduleDll))).ProductVersion
        if ($moduleVersion -ne $fullVersion) { throw "Module version mismatch: $moduleDll is $moduleVersion, GUI is $fullVersion." }
    }
    if ($version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "Invalid build version: $version" }
    if ($ReleaseTag -and $ReleaseTag -cne "v$version") { throw "Tag $ReleaseTag does not match MinVer version v$version." }
    # Bundle the complete standalone tools with the desktop app for a single download.
    foreach ($tool in @('cli', 'mcp')) {
        $toolDestination = Join-Path $publishRoot "app/$tool"
        New-Item -ItemType Directory -Path $toolDestination -Force | Out-Null
        Copy-Item -Path (Join-Path $publishRoot "$tool/*") -Destination $toolDestination -Recurse -Force
    }
    foreach ($module in @(@{ Name = 'FreelancerMissionEditor'; Folder = 'app' }, @{ Name = 'MissionEditorCli'; Folder = 'cli' }, @{ Name = 'MissionEditorMcp'; Folder = 'mcp' })) {
        $archive = Join-Path $releaseRoot "$($module.Name)-$version-win-x64.zip"
        Compress-Archive -Path (Join-Path $publishRoot "$($module.Folder)/*") -DestinationPath $archive -Force
    }
    $checksums = foreach ($archive in Get-ChildItem $releaseRoot -Filter "*-$version-win-x64.zip") {
        '{0}  {1}' -f (Get-FileHash $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $archive.Name
    }
    $checksums | Set-Content (Join-Path $releaseRoot 'checksums.txt') -Encoding ascii
    if ($env:GITHUB_OUTPUT) { "version=$version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8 }
    Write-Host "Packaged version $version in $releaseRoot"
}
finally { Pop-Location }

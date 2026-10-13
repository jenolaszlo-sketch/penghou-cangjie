param(
    [Parameter(Mandatory)][string]$Version,
    [string]$PackageDirectory,
    [string]$BinaryBaselineVersion
)
$ErrorActionPreference = 'Stop'
if (-not $BinaryBaselineVersion) {
    $BinaryBaselineVersion = if ($Version -eq '1.0.0') { '1.0.0-rc.1' } else { '0.1.0-preview.3' }
}
function Invoke-Dotnet { & dotnet @args; if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $args" } }
$repo = Split-Path $PSScriptRoot -Parent
$workspace = Join-Path ([IO.Path]::GetTempPath()) "cangjie-qualification-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $workspace | Out-Null
$sources = '<add key="nuget" value="https://api.nuget.org/v3/index.json" />'
if ($PackageDirectory) {
    $packagePath = (Resolve-Path -LiteralPath $PackageDirectory).Path
    $sources = "<add key=`"release`" value=`"$([System.Security.SecurityElement]::Escape($packagePath))`" />" + $sources
}
"<configuration><packageSources><clear />$sources</packageSources></configuration>" | Set-Content (Join-Path $workspace 'NuGet.Config')
foreach ($name in @('candidate', 'binary')) {
    $target = Join-Path $workspace $name
    New-Item -ItemType Directory $target | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'PackageConsumer/*') $target
    $targetVersion = if ($name -eq 'candidate') { $Version } else { $BinaryBaselineVersion }
    Invoke-Dotnet restore (Join-Path $target 'PackageConsumer.csproj') "-p:QualificationVersion=$targetVersion" --packages (Join-Path $workspace "packages-$name") --configfile (Join-Path $workspace 'NuGet.Config') --no-cache
    Invoke-Dotnet build (Join-Path $target 'PackageConsumer.csproj') -c Release --no-restore "-p:QualificationVersion=$targetVersion"
}
$candidateBin = Join-Path $workspace 'candidate/bin/Release/net10.0'
$binaryBin = Join-Path $workspace 'binary/bin/Release/net10.0'
$binaryHash = (Get-FileHash (Join-Path $binaryBin 'PackageConsumer.dll') -Algorithm SHA256).Hash
Invoke-Dotnet (Join-Path $candidateBin 'PackageConsumer.dll')
foreach ($package in @('Penghou.Cangjie', 'Penghou.Cangjie.Sqlite', 'Penghou.Cangjie.Testing')) {
    Copy-Item (Join-Path $candidateBin "$package.dll") (Join-Path $binaryBin "$package.dll") -Force
}
if ((Get-FileHash (Join-Path $binaryBin 'PackageConsumer.dll') -Algorithm SHA256).Hash -ne $binaryHash) { throw 'Compiled consumer changed during binary qualification.' }
Invoke-Dotnet (Join-Path $binaryBin 'PackageConsumer.dll')

# Exercise the existing cross-store restart test unchanged, replacing only its
# repository project reference with the exact qualified public package.
$integration = Join-Path $workspace 'cross-store'
New-Item -ItemType Directory $integration | Out-Null
Copy-Item (Join-Path $repo 'tests/Penghou.Cangjie.Integration.Tests/*.cs') $integration
Copy-Item (Join-Path $repo 'tests/Penghou.Cangjie.Sqlite.Tests/GraduationContractTests.cs') $integration
$project = Get-Content (Join-Path $repo 'tests/Penghou.Cangjie.Integration.Tests/Penghou.Cangjie.Integration.Tests.csproj') -Raw
$project = $project -replace '<ProjectReference[^>]*/>', "<PackageReference Include=`"Penghou.Cangjie.Sqlite`" Version=`"$Version`" />"
$project | Set-Content (Join-Path $integration 'CrossStore.csproj')
Invoke-Dotnet restore (Join-Path $integration 'CrossStore.csproj') --packages (Join-Path $workspace 'packages-cross-store') --configfile (Join-Path $workspace 'NuGet.Config') --no-cache
Invoke-Dotnet test (Join-Path $integration 'CrossStore.csproj') -c Release --no-restore
$record = [ordered]@{
    version = $Version; dateUtc = [DateTime]::UtcNow.ToString('O'); os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    runtime = (& dotnet --version); sourceCommit = (& git -C $repo rev-parse HEAD)
    source = if ($PackageDirectory) { 'exact local artifacts' } else { 'clean public NuGet restore' }
    packageArtifactHashes = @(Get-ChildItem (Join-Path $workspace 'packages-candidate') -Recurse -Filter '*.nupkg' | Where-Object { $_.Name -like 'penghou.cangjie*' } | ForEach-Object { @{ name = $_.Name; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash } })
    packageAssemblyHashes = @(Get-ChildItem $candidateBin -Filter 'Penghou.Cangjie*.dll' | ForEach-Object { @{ name = $_.Name; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash } })
    binaryBaselineVersion = $BinaryBaselineVersion; unchangedConsumerSha256 = $binaryHash
    result = 'SUPPORTED BY THIS PROBE: conformance, multi-process revisions, process restart/kill rollback, bounds/snapshot integrity, unchanged compiled consumer, packaged Solo/Zhinu restart (public Zhinu 0.1.0-preview.14)'
}
$evidence = Join-Path $repo 'artifacts/qualification'
New-Item -ItemType Directory -Force $evidence | Out-Null
$record | ConvertTo-Json | Set-Content (Join-Path $evidence "qualification-$Version-$([System.Runtime.InteropServices.RuntimeInformation]::OSDescription -replace '[^a-zA-Z0-9]', '_').json")
Write-Output "Qualification passed. Isolated evidence workspace: $workspace"

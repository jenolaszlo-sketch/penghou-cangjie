param(
    [Parameter(Mandatory)][string]$Version,
    [string]$PackageDirectory,
    [string]$ReferencePackageDirectory,
    [string]$BinaryBaselineVersion
)
$ErrorActionPreference = 'Stop'
if (-not $BinaryBaselineVersion) { $BinaryBaselineVersion = if ($Version -eq '1.0.0') { '1.0.0-rc.1' } else { '0.1.0-preview.3' } }
function Invoke-Dotnet { & dotnet @args; if ($LASTEXITCODE -ne 0) { throw "dotnet failed: $args" } }
function Get-Payload($Path) {
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = [ordered]@{}
        foreach ($entry in ($zip.Entries | Sort-Object FullName)) {
            if ($entry.FullName -eq '.signature.p7s') { continue }
            $stream = $entry.Open()
            try { $entries[$entry.FullName] = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) } finally { $stream.Dispose() }
        }
        return ($entries | ConvertTo-Json -Compress)
    } finally { $zip.Dispose() }
}
function Assert-Packages($AssetsPath, $ExpectedVersion, $PackagesRoot, $Reference) {
    $assets = Get-Content $AssetsPath -Raw | ConvertFrom-Json -AsHashtable
    if (@($assets.libraries.Values | Where-Object { $_.type -eq 'project' }).Count) { throw 'Qualification graph contains a project reference.' }
    foreach ($id in @('Penghou.Cangjie', 'Penghou.Cangjie.Sqlite', 'Penghou.Cangjie.Testing')) {
        $entries = @($assets.libraries.Keys | Where-Object { $_ -like "$id/*" })
        if ($entries.Count -ne 1 -or $entries[0] -ne "$id/$ExpectedVersion") { throw "Unexpected package graph for $id : $entries" }
        $cached = Join-Path $PackagesRoot "$($id.ToLowerInvariant())/$ExpectedVersion/$($id.ToLowerInvariant()).$ExpectedVersion.nupkg"
        if (-not (Test-Path -LiteralPath $cached)) { throw "Missing resolved package $cached" }
        if ($Reference) {
            $reviewed = Join-Path $Reference "$id.$ExpectedVersion.nupkg"
            if ((Get-Payload $cached) -cne (Get-Payload $reviewed)) { throw "Resolved $id payload differs from qualified artifact (only .signature.p7s excluded)." }
        }
    }
}
function Invoke-Consumer($Bin, [string[]]$Arguments = @()) {
    $prior = $env:CANGJIE_QUALIFICATION_HASHES
    try {
        $hashes = @{}
        Get-ChildItem $Bin -Filter 'Penghou.Cangjie*.dll' | ForEach-Object { $hashes[$_.Name] = (Get-FileHash $_.FullName -Algorithm SHA256).Hash }
        $env:CANGJIE_QUALIFICATION_HASHES = $hashes | ConvertTo-Json -Compress
        Invoke-Dotnet (Join-Path $Bin 'PackageConsumer.dll') @Arguments
    } finally { $env:CANGJIE_QUALIFICATION_HASHES = $prior }
}
$repo = Split-Path $PSScriptRoot -Parent
$workspace = Join-Path ([IO.Path]::GetTempPath()) "cangjie-qualification-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $workspace | Out-Null
$publicConfig = Join-Path $workspace 'Public.Config'
'<configuration><packageSources><clear/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>' | Set-Content $publicConfig
$candidateConfig = $publicConfig
if ($PackageDirectory) {
    $packagePath = (Resolve-Path -LiteralPath $PackageDirectory).Path
    $candidateConfig = Join-Path $workspace 'Candidate.Config'
    $escaped = [System.Security.SecurityElement]::Escape($packagePath)
    "<configuration><packageSources><clear/><add key=`"release`" value=`"$escaped`"/><add key=`"nuget`" value=`"https://api.nuget.org/v3/index.json`"/></packageSources><packageSourceMapping><packageSource key=`"release`"><package pattern=`"Penghou.Cangjie*`"/></packageSource><packageSource key=`"nuget`"><package pattern=`"*`"/></packageSource></packageSourceMapping></configuration>" | Set-Content $candidateConfig
    $ReferencePackageDirectory = $packagePath
}
if ($ReferencePackageDirectory) { $ReferencePackageDirectory = (Resolve-Path -LiteralPath $ReferencePackageDirectory).Path }
foreach ($name in @('candidate', 'binary')) {
    $target = Join-Path $workspace $name
    New-Item -ItemType Directory $target | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'PackageConsumer/*') $target
    $targetVersion = if ($name -eq 'candidate') { $Version } else { $BinaryBaselineVersion }
    $config = if ($name -eq 'candidate') { $candidateConfig } else { $publicConfig }
    $cache = Join-Path $workspace "packages-$name"
    Invoke-Dotnet restore (Join-Path $target 'PackageConsumer.csproj') "-p:QualificationVersion=$targetVersion" --packages $cache --configfile $config --no-cache
    Assert-Packages (Join-Path $target 'obj/project.assets.json') $targetVersion $cache $(if ($name -eq 'candidate') { $ReferencePackageDirectory })
    Invoke-Dotnet build (Join-Path $target 'PackageConsumer.csproj') -c Release --no-restore "-p:QualificationVersion=$targetVersion"
}
$candidateBin = Join-Path $workspace 'candidate/bin/Release/net10.0'
$binaryBin = Join-Path $workspace 'binary/bin/Release/net10.0'
$binaryHash = (Get-FileHash (Join-Path $binaryBin 'PackageConsumer.dll') -Algorithm SHA256).Hash
Invoke-Consumer $binaryBin
$retainedPath = Join-Path $workspace 'retained-schema4.db'
Invoke-Consumer $binaryBin @('seed', $retainedPath)
Invoke-Consumer $candidateBin
foreach ($package in @('Penghou.Cangjie', 'Penghou.Cangjie.Sqlite', 'Penghou.Cangjie.Testing')) { Copy-Item (Join-Path $candidateBin "$package.dll") (Join-Path $binaryBin "$package.dll") -Force }
if ((Get-FileHash (Join-Path $binaryBin 'PackageConsumer.dll') -Algorithm SHA256).Hash -ne $binaryHash) { throw 'Compiled consumer changed during binary qualification.' }
Invoke-Consumer $binaryBin
Invoke-Consumer $binaryBin @('read-seed', $retainedPath)
Invoke-Consumer $candidateBin @('read-seed', $retainedPath)
$integration = Join-Path $workspace 'cross-store'
New-Item -ItemType Directory $integration | Out-Null
Copy-Item (Join-Path $repo 'tests/Penghou.Cangjie.Integration.Tests/*.cs') $integration
Copy-Item (Join-Path $repo 'tests/Penghou.Cangjie.Sqlite.Tests/GraduationContractTests.cs') $integration
$project = Get-Content (Join-Path $repo 'tests/Penghou.Cangjie.Integration.Tests/Penghou.Cangjie.Integration.Tests.csproj') -Raw
$project = $project -replace '<ProjectReference[^>]*/>', "<PackageReference Include=`"Penghou.Cangjie.Sqlite`" Version=`"$Version`" />"
$project | Set-Content (Join-Path $integration 'CrossStore.csproj')
$crossCache = Join-Path $workspace 'packages-cross-store'
Invoke-Dotnet restore (Join-Path $integration 'CrossStore.csproj') --packages $crossCache --configfile $candidateConfig --no-cache
$crossAssets = Get-Content (Join-Path $integration 'obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
if (@($crossAssets.libraries.Values | Where-Object { $_.type -eq 'project' }).Count -or -not $crossAssets.libraries.ContainsKey("Penghou.Cangjie.Sqlite/$Version") -or -not $crossAssets.libraries.ContainsKey("Penghou.Cangjie/$Version")) { throw 'Cross-store package graph is not exact/project-free.' }
foreach ($id in @('Penghou.Cangjie', 'Penghou.Cangjie.Sqlite')) {
    if ($ReferencePackageDirectory) {
        $cached = Join-Path $crossCache "$($id.ToLowerInvariant())/$Version/$($id.ToLowerInvariant()).$Version.nupkg"
        if ((Get-Payload $cached) -cne (Get-Payload (Join-Path $ReferencePackageDirectory "$id.$Version.nupkg"))) { throw "Cross-store payload differs for $id" }
    }
}
Invoke-Dotnet test (Join-Path $integration 'CrossStore.csproj') -c Release --no-restore
$qualifiedRuntime = & dotnet (Join-Path $candidateBin 'PackageConsumer.dll') runtime
if ($LASTEXITCODE -ne 0) { throw 'Qualified consumer runtime query failed.' }
$record = [ordered]@{
    version = $Version; dateUtc = [DateTime]::UtcNow.ToString('O'); os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    repository = 'https://github.com/jenolaszlo-sketch/penghou-cangjie'
    sourceRef = if ($env:GITHUB_REF) { $env:GITHUB_REF } else { (& git -C $repo branch --show-current) }
    sdk = (& dotnet --version); runtime = $qualifiedRuntime; targetFramework = 'net10.0'
    sourceCommit = (& git -C $repo rev-parse HEAD)
    source = if ($PackageDirectory) { 'mapped exact local artifacts' } else { 'clean public NuGet restore' }
    payloadComparison = if ($ReferencePackageDirectory) { 'all ZIP entry names and SHA256 payloads identical; only .signature.p7s excluded' } else { 'reference not supplied; public restore alone' }
    packageArtifactHashes = @(Get-ChildItem (Join-Path $workspace 'packages-candidate') -Recurse -Filter '*.nupkg' | Where-Object { $_.Name -like 'penghou.cangjie*' } | ForEach-Object { @{ name = $_.Name; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash } })
    packageAssemblyHashes = @(Get-ChildItem $candidateBin -Filter 'Penghou.Cangjie*.dll' | ForEach-Object { @{ name = $_.Name; sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash } })
    binaryBaselineVersion = $BinaryBaselineVersion; unchangedConsumerSha256 = $binaryHash; retainedBaselineSnapshot = $retainedPath
    result = 'SUPPORTED BY THIS PROBE: exact/project-free graph and package payloads; baseline execution; retained baseline schema4 snapshot; runtime assembly hashes; conformance; multi-process revisions; process restart/kill rollback; bounds/snapshot integrity; unchanged compiled consumer; packaged Solo/Zhinu restart (public Zhinu 0.1.0-preview.14)'
}
$evidence = Join-Path $repo 'artifacts/qualification'
New-Item -ItemType Directory -Force $evidence | Out-Null
$record | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $evidence "qualification-$Version-$([System.Runtime.InteropServices.RuntimeInformation]::OSDescription -replace '[^a-zA-Z0-9]', '_').json")
Write-Output "Qualification passed. Isolated evidence workspace: $workspace"

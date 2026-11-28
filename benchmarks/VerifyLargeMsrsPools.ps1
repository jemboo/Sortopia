param([Parameter(Mandatory)][string]$BuildRoot, [Parameter(Mandatory)][string]$Runs, [Parameter(Mandatory)][string]$Fixtures)
$ErrorActionPreference = 'Stop'
$BuildRoot = (Resolve-Path -LiteralPath $BuildRoot).Path
$Runs = (Resolve-Path -LiteralPath $Runs).Path
$Fixtures = (Resolve-Path -LiteralPath $Fixtures).Path
$rows = @(Get-Content -LiteralPath (Join-Path $Runs 'measurements.json') -Raw | ConvertFrom-Json)
$resumes = @(Get-Content -LiteralPath (Join-Path $Runs 'resumes.json') -Raw | ConvertFrom-Json)
if ($rows.Count -ne 24 -or $resumes.Count -ne 18) { throw 'The full matrix requires 24 primary runs and 18 resume runs.' }
function Json($value) { ConvertTo-Json -InputObject $value -Depth 15 -Compress }
function Identity($row) {
    Json ([ordered]@{ InputId=$row.InputId; InputHash=$row.InputHash; Snapshots=$row.SemanticSnapshots
        Details=$row.Details; Population=$row.PopulationHistory
        Summaries=@($row.Files | Where-Object { $_.Path.Contains('SorterPoolSummarySet') }) })
}
$fileCount = 0
function CheckFiles($row, $directory) {
    foreach ($file in $row.Files) {
        $path = Join-Path $directory $file.Path
        if (($null -ne $file.Bytes -and (Get-Item -LiteralPath $path).Length -ne $file.Bytes) -or (Get-FileHash -LiteralPath $path).Hash -cne $file.Hash) {
            throw "File changed: $path"
        }
        $script:fileCount++
    }
}
foreach ($width in @(24,32,64)) {
    $fixture = Get-Content -LiteralPath (Join-Path $Fixtures "cc-$width/measurement.json") -Raw | ConvertFrom-Json
    CheckFiles $fixture (Join-Path $Fixtures "cc-$width")
    foreach ($population in @(1024,2048)) {
        $case = @($rows | Where-Object { $_.Width -eq $width -and $_.MembersPerPool -eq $population })
        if ($case.Count -ne 4) { throw "Incomplete case: $width/$population" }
        $anchor = $case | Where-Object { $_.Build -eq 'baseline' -and $_.Round -eq 1 }
        $expected = Identity $anchor
        foreach ($row in $case) {
            if ((Identity $row) -cne $expected) { throw 'Primary identity differs.' }
            if ($row.InputId -cne $fixture.InputId -or $row.InputHash -cne $fixture.InputHash) { throw 'Fixture identity differs.' }
            foreach ($generation in $row.PopulationHistory) {
                if ($generation.Members.Count -ne 2 -or @($generation.Members | Where-Object { $_ -ne $population }).Count -ne 0) { throw 'Population size differs from the requested workload.' }
            }
            CheckFiles $row $row.Directory
        }
        foreach ($build in @('baseline','optimized')) {
            $repeat = @($case | Where-Object Build -eq $build)
            if ((Json $repeat[0].Files) -cne (Json $repeat[1].Files)) { throw 'Repeated complete files differ.' }
        }
        $resumeCase = @($resumes | Where-Object { $_.Width -eq $width -and $_.MembersPerPool -eq $population })
        if ($resumeCase.Count -ne 3) { throw 'Missing native or legacy resume.' }
        $resumeIdentity = Identity $resumeCase[0]
        foreach ($row in $resumeCase) {
            if ((Identity $row) -cne $resumeIdentity) { throw 'Native/legacy resume differs.' }
            $expectedSnapshots = @($anchor.SemanticSnapshots | Where-Object Generation -ge $row.ResumeGeneration)
            if ((Json $row.SemanticSnapshots) -cne (Json $expectedSnapshots)) { throw 'Resumed trajectory differs from uninterrupted evolution.' }
            $sameBuild = $case | Where-Object { $_.Build -eq $row.Build -and $_.Round -eq 1 }
            foreach ($file in $row.Files) {
                if ((Json $file) -cne (Json ($sameBuild.Files | Where-Object Path -eq $file.Path))) { throw 'Resumed complete checkpoint/summary file differs.' }
            }
            CheckFiles $row $row.Directory
        }
    }
}
$buildFiles = [ordered]@{}
foreach ($build in @('baseline','optimized')) {
    $manifest = @(Get-Content -LiteralPath (Join-Path $BuildRoot "$build-harness-files.json") -Raw | ConvertFrom-Json)
    foreach ($file in $manifest) {
        $path = Join-Path $BuildRoot "$build-harness/bin/Release/net10.0/$($file.Path)"
        if ((Get-FileHash -LiteralPath $path).Hash -cne $file.Hash) { throw "Benchmark build changed: $path" }
    }
    $buildFiles[$build] = $manifest.Count
}
$report = [ordered]@{ PrimaryRuns=$rows.Count; FreshProcessResumeRuns=$resumes.Count; Workloads=6
    SavedFilesRehashed=$fileCount; FixtureIdentitiesVerified=3; EveryPoolRetainedRequestedMemberCount=$true
    SemanticCheckpointsMatch=$true; SummaryFilesByteIdentical=$true; ReconstructedDetailMatches=$true
    NativeAndLegacyResumeMatchUninterrupted=$true; ResumedFilesMatchUninterruptedBuild=$true; FrozenHarnessFiles=$buildFiles }
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $BuildRoot 'final-verification.json')
$report | ConvertTo-Json -Depth 8

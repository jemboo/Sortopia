param(
    [Parameter(Mandatory)][string]$BuildRoot,
    [Parameter(Mandatory)][string]$Fixtures,
    [Parameter(Mandatory)][string]$Output,
    [int[]]$Widths = @(24,32,64),
    [int[]]$Populations = @(1024,2048),
    [int]$Repetitions = 2,
    [int]$Workers = 25
)
$ErrorActionPreference = 'Stop'
$BuildRoot = (Resolve-Path -LiteralPath $BuildRoot).Path
$Fixtures = (Resolve-Path -LiteralPath $Fixtures).Path
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw 'Use a fresh output directory.' }
New-Item -ItemType Directory -Path $Output | Out-Null
$rows = [Collections.Generic.List[object]]::new()
$resumes = [Collections.Generic.List[object]]::new()
function Json($value) { ConvertTo-Json -InputObject $value -Depth 15 -Compress }
function Identity($row) {
    Json ([ordered]@{ InputId=$row.InputId; InputHash=$row.InputHash; Snapshots=$row.SemanticSnapshots
        Details=$row.Details; Population=$row.PopulationHistory
        Summaries=@($row.Files | Where-Object { $_.Path.Contains('SorterPoolSummarySet') }) })
}
function RunCase($width,$population,$round,$build,$mode,$sourceRun,$label) {
    $count = if ($width -eq 24) { 531441 } else { 4194307 }
    $destination = Join-Path $Output $label
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $BuildRoot "$build-harness/bin/Release/net10.0/LargeMsrsPools.exe"))
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['DOTNET_PROCESSOR_COUNT'] = [string]$Workers
    foreach ($arg in @($width,$count,$Workers,$population,3,128,$mode,$destination,(Join-Path $Fixtures "cc-$width"))) {
        $start.ArgumentList.Add([string]$arg)
    }
    if ($mode -eq 'resume') { $start.ArgumentList.Add($sourceRun) }
    $child = [Diagnostics.Process]::new()
    $child.StartInfo = $start
    Write-Host "Starting $label."
    $log = [Collections.Generic.List[string]]::new()
    $clock = [Diagnostics.Stopwatch]::StartNew()
    try {
        $child.Start() | Out-Null
        $stderr = $child.StandardError.ReadToEndAsync()
        $line = $child.StandardOutput.ReadLineAsync()
        $outputDone = $false
        $lastStatus = -10.0
        while (-not $child.HasExited -or -not $outputDone) {
            if (-not $outputDone -and $line.Wait(1000)) {
                $value = $line.GetAwaiter().GetResult()
                if ($null -eq $value) { $outputDone = $true }
                else { $log.Add($value); Write-Host "$label : $value"; $line = $child.StandardOutput.ReadLineAsync() }
            }
            elseif ($outputDone) { $child.WaitForExit(1000) | Out-Null }
            if ($clock.Elapsed.TotalSeconds - $lastStatus -ge 10 -and -not $child.HasExited) {
                $child.Refresh()
                [ordered]@{ Label=$label; Pid=$child.Id; ElapsedSeconds=$clock.Elapsed.TotalSeconds
                    CpuSeconds=$child.TotalProcessorTime.TotalSeconds; PrivateMiB=$child.PrivateMemorySize64/1MB
                    PeakResidentMiB=$child.PeakWorkingSet64/1MB; LastProgress=($log | Select-Object -Last 1) } |
                    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'status.json')
                $lastStatus = $clock.Elapsed.TotalSeconds
            }
        }
        $child.WaitForExit()
        [IO.File]::WriteAllLines("$destination-stdout.log",$log)
        [IO.File]::WriteAllText("$destination-stderr.log",$stderr.GetAwaiter().GetResult())
        if ($child.ExitCode -ne 0) { throw "Run failed: $destination-stderr.log" }
    }
    finally {
        if (-not $child.HasExited) { $child.Kill($true); $child.WaitForExit() }
        $child.Dispose()
    }
    $row = Get-Content -LiteralPath (Join-Path $destination 'measurement.json') -Raw | ConvertFrom-Json
    $row | Add-Member -NotePropertyName Build -NotePropertyValue $build
    $row | Add-Member -NotePropertyName Round -NotePropertyValue $round
    $row | Add-Member -NotePropertyName Directory -NotePropertyValue $destination
    Write-Host ("Finished {0}: {1:N1}s; peak resident {2:N1} MiB; committed {3:N1} MiB." -f $label,$row.Seconds,($row.PeakWorkingSetBytes/1MB),($row.PeakCommittedBytes/1MB))
    return $row
}
foreach ($width in $Widths) {
    foreach ($population in $Populations) {
        $expected = $null
        $filesByBuild = @{}
        for ($round=1; $round -le $Repetitions; $round++) {
            $builds = if ($round % 2) { @('baseline','optimized') } else { @('optimized','baseline') }
            foreach ($build in $builds) {
                $row = RunCase $width $population $round $build 'measure' '' "cc-$width-pool-$population-round-$round-$build"
                $identity = Identity $row
                if ($null -eq $expected) { $expected = $identity }
                elseif ($identity -cne $expected) { throw "Evolution identity differs at $width/$population/$round/$build." }
                if ($filesByBuild.ContainsKey($build) -and $filesByBuild[$build] -cne (Json $row.Files)) { throw 'Repeated saved files differ.' }
                $filesByBuild[$build] = Json $row.Files
                $rows.Add($row)
                ConvertTo-Json -InputObject $rows.ToArray() -Depth 15 | Set-Content -LiteralPath (Join-Path $Output 'measurements.json')
                Write-Host 'Input, population, semantic checkpoints, summary files, and selected comparator detail verified.'
            }
        }
    }
}
# Resume all six workloads in fresh processes, independently of primary timings.
foreach ($width in $Widths) {
    foreach ($population in $Populations) {
        $baselineResume = $null
        foreach ($pair in @(@('baseline','baseline'),@('optimized','optimized'),@('optimized','baseline'))) {
            $build,$sourceBuild = $pair
            $anchor = $rows | Where-Object { $_.Width -eq $width -and $_.MembersPerPool -eq $population -and $_.Round -eq 1 -and $_.Build -eq $sourceBuild }
            $row = RunCase $width $population 1 $build 'resume' $anchor.Directory "cc-$width-pool-$population-resume-$build-from-$sourceBuild"
            if ($row.InputId -cne $anchor.InputId -or $row.InputHash -cne $anchor.InputHash) { throw 'Resume inputs differ.' }
            $expectedSnapshots = @($anchor.SemanticSnapshots | Where-Object Generation -ge $row.ResumeGeneration)
            if ((Json $row.SemanticSnapshots) -cne (Json $expectedSnapshots)) { throw 'Resumed checkpoint trajectory differs.' }
            foreach ($file in $row.Files | Where-Object { $_.Path.Contains('SorterPoolSummarySet') }) {
                $expectedFile = $anchor.Files | Where-Object Path -eq $file.Path
                if ((Json $file) -cne (Json $expectedFile)) { throw 'Resumed summary file differs.' }
            }
            if ($build -eq $sourceBuild) {
                foreach ($file in $row.Files) {
                    if ((Json $file) -cne (Json ($anchor.Files | Where-Object Path -eq $file.Path))) { throw 'Native resume checkpoint bytes differ.' }
                }
            }
            if ($null -eq $baselineResume) { $baselineResume = Identity $row }
            elseif ((Identity $row) -cne $baselineResume) { throw 'V2-to-V1 resumed trajectory or reconstructed detail differs.' }
            $resumes.Add($row)
            ConvertTo-Json -InputObject $resumes.ToArray() -Depth 15 | Set-Content -LiteralPath (Join-Path $Output 'resumes.json')
            Write-Host 'Fresh-process checkpoint resume verified.'
        }
    }
}
[ordered]@{ Complete=$true; PrimaryRuns=$rows.Count; ResumeRuns=$resumes.Count } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'status.json')

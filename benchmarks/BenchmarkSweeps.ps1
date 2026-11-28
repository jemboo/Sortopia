param(
    [Parameter(Mandatory)][string]$Baseline,
    [Parameter(Mandatory)][string]$Candidate,
    [Parameter(Mandatory)][string]$Templates,
    [Parameter(Mandatory)][string]$Output,
    [int]$Repetitions = 3,
    [string[]]$Scenarios = @('fresh-grid', 'uneven-tail', 'mostly-complete'),
    [int]$CpuPercent = 50,
    [string]$Memory = '8GiB',
    [string]$HardMemoryCap,
    [string]$Jobs = 'auto',
    [switch]$CandidateJobMetrics,
    [switch]$BaselineJobMetrics,
    [ValidateSet('baseline', 'dynamic')][string[]]$Builds = @('baseline', 'dynamic'),
    [string]$ReferenceHashes,
    [switch]$ContinueOnFailure
)
$ErrorActionPreference = 'Stop'
$Baseline = (Resolve-Path -LiteralPath $Baseline).Path
$Candidate = (Resolve-Path -LiteralPath $Candidate).Path
$Templates = (Resolve-Path -LiteralPath $Templates).Path
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw 'Use a new output directory; previous measurements are preserved.' }
New-Item -ItemType Directory -Path $Output | Out-Null
$manifest = Get-Content -LiteralPath (Join-Path $Templates 'manifest.json') -Raw | ConvertFrom-Json
$rows = [Collections.Generic.List[object]]::new()

function Invoke-Batch([string]$Executable, [string]$Folder, [string[]]$Files, [string]$Label) {
    $selection = Join-Path $Folder "$Label-selection.json"
    $paths = @($Files | ForEach-Object { Join-Path (Join-Path $Folder 'runParameters') $_ })
    ConvertTo-Json -InputObject $paths | Set-Content -LiteralPath $selection -Encoding utf8
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('execute-results', '--parameters-list', $selection, '--jobs', $Jobs, '--cpu-percent', "$CpuPercent", '--memory', $Memory)) {
        $start.ArgumentList.Add($argument)
    }
    if ($HardMemoryCap -and $Executable -eq $Candidate) {
        $start.ArgumentList.Add('--memory-cap')
        $start.ArgumentList.Add($HardMemoryCap)
    }
    $jobMetrics = Join-Path $Folder "$Label-job-metrics.json"
    $collectJobMetrics = ($CandidateJobMetrics -and $Executable -eq $Candidate) -or ($BaselineJobMetrics -and $Executable -eq $Baseline)
    if ($collectJobMetrics) {
        $start.ArgumentList.Add('--execution-metrics')
        $start.ArgumentList.Add($jobMetrics)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $peakPrivate = 0L
    $peakWorking = 0L
    $memoryTargetBytes = $null
    $hardCapBytes = $null
    try {
        $process.Start() | Out-Null
        $stdoutFile = [IO.File]::Open((Join-Path $Folder "$Label-stdout.log"), [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
        $stderrFile = [IO.File]::Open((Join-Path $Folder "$Label-stderr.log"), [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::Read)
        $stdout = $process.StandardOutput.BaseStream.CopyToAsync($stdoutFile)
        $stderr = $process.StandardError.BaseStream.CopyToAsync($stderrFile)
        # Memory is sampled only by this benchmark harness, never by the Results UI.
        while (-not $process.WaitForExit(25)) {
            $process.Refresh()
            $peakPrivate = [Math]::Max($peakPrivate, $process.PrivateMemorySize64)
            $peakWorking = [Math]::Max($peakWorking, $process.PeakWorkingSet64)
        }
        $clock.Stop()
        $cpu = $process.TotalProcessorTime.TotalSeconds
        $stdout.GetAwaiter().GetResult()
        $stderr.GetAwaiter().GetResult()
        if ($collectJobMetrics) {
            $job = Get-Content -LiteralPath $jobMetrics -Raw | ConvertFrom-Json
            $cpu = $job.CpuSeconds
            $peakPrivate = $job.PeakCommittedBytes
            if ($job.TotalProcesses -lt 1) { throw 'The limited job must include the execution process.' }
            $memoryTargetBytes = if ($null -ne $job.MemoryTargetBytes) { $job.MemoryTargetBytes } else { 0L }
            $hardCapBytes = if ($null -ne $job.HardMemoryLimitBytes) { $job.HardMemoryLimitBytes } else { $job.TotalMemoryBytes }
            if ($hardCapBytes -gt 0 -and $peakPrivate -gt $hardCapBytes) { throw 'Job memory exceeded the selected hard cap.' }
            # Launcher-only working set is not representative of the batch.
            $peakWorking = $null
        }
        [pscustomobject]@{ Seconds = $clock.Elapsed.TotalSeconds; CpuSeconds = $cpu; PeakPrivateBytes = $peakPrivate; PeakWorkingSetBytes = $peakWorking; MemoryTargetBytes = $memoryTargetBytes; HardMemoryLimitBytes = $hardCapBytes; ExitCode = $process.ExitCode } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Folder "$Label-resources.json")
        if ($process.ExitCode -ne 0 -and -not $ContinueOnFailure) { throw "CLI failed ($($process.ExitCode)); see $Folder/$Label-stderr.log" }
        [pscustomobject]@{ Seconds = $clock.Elapsed.TotalSeconds; CpuSeconds = $cpu; PeakPrivateBytes = $peakPrivate; PeakWorkingSetBytes = $peakWorking; MemoryTargetBytes = $memoryTargetBytes; HardMemoryLimitBytes = $hardCapBytes; ExitCode = $process.ExitCode }
    }
    finally {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        if ($null -ne $stdoutFile) { $stdoutFile.Dispose() }
        if ($null -ne $stderrFile) { $stderrFile.Dispose() }
        $process.Dispose()
    }
}

function Get-ResultHashes([string]$Folder) {
    $root = Join-Path $Folder 'data/results'
    if (-not (Test-Path -LiteralPath $root)) { return }
    @(Get-ChildItem -LiteralPath $root -File -Recurse -Filter '*.msgpack' | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($root, $_.FullName); Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}

foreach ($scenario in $manifest | Where-Object { $_.Scenario -in $Scenarios }) {
    $name = $scenario.Scenario
    $template = Join-Path $Templates $name
    if ($scenario.Precomplete -gt 0) {
        $prepared = Join-Path $Output "$name-prepared"
        Copy-Item -LiteralPath $template -Destination $prepared -Recurse
        Write-Output "Preparing $name outside the timed trials."
        Invoke-Batch $Baseline $prepared @($scenario.Files.File | Select-Object -First $scenario.Precomplete) 'prepare' | Out-Null
        $template = $prepared
    }
    $expectedHashes = if ($ReferenceHashes) { ConvertTo-Json -InputObject @(Get-Content -LiteralPath $ReferenceHashes -Raw | ConvertFrom-Json) -Compress } else { $null }
    for ($round = 1; $round -le $Repetitions; $round++) {
        # Alternate order to reduce systematic thermal/background-load bias.
        $buildOrder = if ($round % 2) { @('baseline', 'dynamic') } else { @('dynamic', 'baseline') }
        foreach ($build in $buildOrder | Where-Object { $_ -in $Builds }) {
            $trial = Join-Path $Output "$name-$round-$build"
            Copy-Item -LiteralPath $template -Destination $trial -Recurse
            Write-Output "Starting $name round $round $build."
            $executable = if ($build -eq 'baseline') { $Baseline } else { $Candidate }
            $timing = Invoke-Batch $executable $trial @($scenario.Files.File) 'trial'
            $hashes = @(Get-ResultHashes $trial)
            $serialized = ConvertTo-Json -InputObject $hashes -Compress
            $identical = 0
            if ($timing.ExitCode -eq 0) {
                if ($null -eq $expectedHashes) { $expectedHashes = $serialized }
                else {
                    if ($serialized -cne $expectedHashes) { throw "Result bytes differ: $trial" }
                    $identical = $hashes.Count
                }
            }
            ConvertTo-Json -InputObject $hashes | Set-Content -LiteralPath (Join-Path $trial 'result-hashes.json')
            $row = [pscustomobject]@{
                Scenario = $name; Round = $round; Build = $build; CpuPercent = $CpuPercent; Memory = $Memory; Jobs = $Jobs
                MemoryTargetBytes = $timing.MemoryTargetBytes; HardMemoryLimitBytes = $timing.HardMemoryLimitBytes
                Seconds = $timing.Seconds; CpuSeconds = $timing.CpuSeconds
                PeakPrivateBytes = $timing.PeakPrivateBytes; PeakWorkingSetBytes = $timing.PeakWorkingSetBytes
                ExitCode = $timing.ExitCode; SavedOutputFiles = $hashes.Count; IdenticalOutputFiles = $identical
            }
            $rows.Add($row)
            ConvertTo-Json -InputObject @($rows.ToArray()) | Set-Content -LiteralPath (Join-Path $Output 'measurements.json')
            Write-Output ("Finished {0}: exit {1}, {2:N2}s, CPU {3:N2}s, peak private {4:N2} GiB, {5} saved outputs." -f $build, $row.ExitCode, $row.Seconds, $row.CpuSeconds, ($row.PeakPrivateBytes / 1GB), $hashes.Count)
        }
    }
}

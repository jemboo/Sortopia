param(
    [Parameter(Mandatory)][string]$BuildRoot,
    [Parameter(Mandatory)][string]$Output,
    [int[]]$Widths = @(24, 32, 64),
    [int]$LargeTestCount = 4194307,
    [int]$MembersPerPool = 64,
    [int]$Generations = 3,
    [int]$Stages = 128,
    [int]$Workers = 8,
    [int]$Repetitions = 3,
    [ValidateSet('evolve', 'prepare', 'load-evolve')][string]$Mode = 'evolve',
    [string]$InputFixtures
)
$ErrorActionPreference = 'Stop'
$BuildRoot = (Resolve-Path -LiteralPath $BuildRoot).Path
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw 'Use a fresh benchmark output directory.' }
New-Item -ItemType Directory -Path $Output | Out-Null
$rows = [Collections.Generic.List[object]]::new()
foreach ($width in $Widths) {
    $count = if ($width -eq 24) { 531441 } else { $LargeTestCount }
    $expected = $null
    for ($round = 1; $round -le $Repetitions; $round++) {
        $builds = if ($round % 2 -eq 1) { @('baseline', 'candidate') } else { @('candidate', 'baseline') }
        foreach ($build in $builds) {
            $destination = Join-Path $Output "cc-$width-round-$round-$build"
            $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $BuildRoot "$build-harness/bin/Release/net10.0/MsrsInputs.exe"))
            $start.UseShellExecute = $false
            $start.CreateNoWindow = $true
            $start.RedirectStandardOutput = $true
            $start.RedirectStandardError = $true
            $start.Environment['DOTNET_PROCESSOR_COUNT'] = '8'
            foreach ($arg in @($width, $count, $Workers, $MembersPerPool, $Generations, $Stages, $Mode, $destination)) {
                $start.ArgumentList.Add([string]$arg)
            }
            if ($Mode -eq 'load-evolve') {
                if (-not $InputFixtures) { throw 'Provide InputFixtures for load-evolve.' }
                $start.ArgumentList.Add((Join-Path ([IO.Path]::GetFullPath($InputFixtures)) "cc-$width"))
            }
            $child = [Diagnostics.Process]::new()
            $child.StartInfo = $start
            Write-Output "Starting $width channels round $round $build ($count tests, $Workers workers)."
            try {
                $child.Start() | Out-Null
                $stdout = $child.StandardOutput.ReadToEndAsync()
                $stderr = $child.StandardError.ReadToEndAsync()
                $child.WaitForExit()
                [IO.File]::WriteAllText("$destination-stdout.log", $stdout.GetAwaiter().GetResult())
                [IO.File]::WriteAllText("$destination-stderr.log", $stderr.GetAwaiter().GetResult())
                if ($child.ExitCode -ne 0) { throw "Benchmark failed: $destination-stderr.log" }
            }
            finally {
                if (-not $child.HasExited) { $child.Kill($true); $child.WaitForExit() }
                $child.Dispose()
            }
            $measurement = Get-Content -LiteralPath (Join-Path $destination 'measurement.json') -Raw | ConvertFrom-Json
            $identity = [ordered]@{ InputId = $measurement.InputId; InputHash = $measurement.InputHash; Files = $measurement.Files } | ConvertTo-Json -Depth 10 -Compress
            if ($null -eq $expected) { $expected = $identity }
            elseif ($expected -cne $identity) { throw "Input or saved output identity differs: $destination" }
            $measurement | Add-Member -NotePropertyName Build -NotePropertyValue $build
            $measurement | Add-Member -NotePropertyName Round -NotePropertyValue $round
            $rows.Add($measurement)
            ConvertTo-Json -InputObject $rows.ToArray() -Depth 10 | Set-Content -LiteralPath (Join-Path $Output 'measurements.json')
            Write-Output ("Finished {0}: {1:N2}s, preparation {2:N2}s, peak commit {3:N1} MiB, working set {4:N1} MiB, {5} identical saved files." -f $build, $measurement.Seconds, $measurement.PreparationSeconds, ($measurement.PeakCommittedBytes / 1MB), ($measurement.PeakWorkingSetBytes / 1MB), $measurement.Files.Count)
        }
    }
}

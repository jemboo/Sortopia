param(
    [Parameter(Mandatory)][string]$BuildRoot,
    [Parameter(Mandatory)][string]$Fixtures,
    [Parameter(Mandatory)][string]$Output,
    [int[]]$Widths = @(24, 32, 64),
    [int]$Repetitions = 2
)
$ErrorActionPreference = 'Stop'
$BuildRoot = (Resolve-Path -LiteralPath $BuildRoot).Path
$Fixtures = (Resolve-Path -LiteralPath $Fixtures).Path
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw 'Use a fresh output directory.' }
New-Item -ItemType Directory -Path $Output | Out-Null
$rows = [Collections.Generic.List[object]]::new()
foreach ($width in $Widths) {
    $count = if ($width -eq 24) { 531441 } else { 4194307 }
    $expected = $null
    for ($round = 1; $round -le $Repetitions; $round++) {
        $builds = if ($round % 2) { @('baseline', 'v1', 'optimized') } else { @('optimized', 'v1', 'baseline') }
        foreach ($build in $builds) {
            $destination = Join-Path $Output "cc-$width-round-$round-$build"
            $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $BuildRoot "$build-harness/bin/Release/net10.0/CompactEvolution.exe"))
            $start.UseShellExecute = $false
            $start.CreateNoWindow = $true
            $start.RedirectStandardOutput = $true
            $start.RedirectStandardError = $true
            $start.Environment['DOTNET_PROCESSOR_COUNT'] = '8'
            foreach ($arg in @($width, $count, 8, 64, 3, 128, 'measure', $destination, (Join-Path $Fixtures "cc-$width"))) {
                $start.ArgumentList.Add([string]$arg)
            }
            $child = [Diagnostics.Process]::new()
            $child.StartInfo = $start
            Write-Output "Starting $width channels round $round $build."
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
            $row = Get-Content -LiteralPath (Join-Path $destination 'measurement.json') -Raw | ConvertFrom-Json
            $identity = [ordered]@{
                InputId=$row.InputId; InputHash=$row.InputHash; Snapshots=$row.SemanticSnapshots; Details=$row.Details
                Summaries=@($row.Files | Where-Object { $_.Path.Contains('SorterPoolSummarySet') })
            } | ConvertTo-Json -Depth 12 -Compress
            if ($null -eq $expected) { $expected = $identity }
            elseif ($expected -cne $identity) { throw "Semantic snapshot, summary, or reconstructed detail differs: $destination" }
            $row | Add-Member -NotePropertyName Build -NotePropertyValue $build
            $row | Add-Member -NotePropertyName Round -NotePropertyValue $round
            $rows.Add($row)
            ConvertTo-Json -InputObject $rows.ToArray() -Depth 12 | Set-Content -LiteralPath (Join-Path $Output 'measurements.json')
            Write-Output ("Finished {0}: {1:N2}s, peak resident {2:N1} MiB, allocated {3:N2} GiB, checkpoints {4:N1} KiB; identity verified." -f $build, $row.Seconds, ($row.PeakWorkingSetBytes / 1MB), ($row.AllocatedBytes / 1GB), ($row.SnapshotBytes / 1KB))
        }
    }
}

param(
    [Parameter(Mandatory)][string]$Template,
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][string]$ExpectedHashes,
    [int]$Workers = 16,
    [int[]]$HeapLimits = @(4, 8),
    [string]$Executable = 'benchmarks/Sortopia.CpuBench/bin/Release/net10.0/Sortopia.CpuBench.exe'
)
$ErrorActionPreference = 'Stop'
$Template = (Resolve-Path -LiteralPath $Template).Path
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$Output = [IO.Path]::GetFullPath($Output)
$expected = Get-Content -LiteralPath $ExpectedHashes -Raw
if (Test-Path -LiteralPath $Output) { throw 'Use a new output directory.' }
New-Item -ItemType Directory -Path $Output | Out-Null
$rows = [Collections.Generic.List[object]]::new()
$trials = @(
    foreach ($limit in $HeapLimits) { [pscustomobject]@{ Round = 1; Heaps = $limit } }
    for ($index = $HeapLimits.Count - 1; $index -ge 0; $index--) { [pscustomobject]@{ Round = 2; Heaps = $HeapLimits[$index] } }
)
foreach ($trial in $trials) {
    $round, $heaps = $trial.Round, $trial.Heaps
    $folder = Join-Path $Output "round-$round-heaps-$heaps"
    Write-Output "Starting server GC trial $round, heap limit $heaps, $Workers workers, 50% CPU, 8 GiB memory."
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['DOTNET_gcServer'] = '1'
    # .NET reads environment-based GC heap counts as hexadecimal.
    $start.Environment['DOTNET_GCHeapCount'] = $heaps.ToString('x')
    foreach ($argument in @($Template, $folder, "$Workers")) { $start.ArgumentList.Add($argument) }
    $child = [Diagnostics.Process]::new()
    $child.StartInfo = $start
    try {
        $child.Start() | Out-Null
        $stdout = $child.StandardOutput.ReadToEndAsync()
        $stderr = $child.StandardError.ReadToEndAsync()
        $child.WaitForExit()
        $stdout.GetAwaiter().GetResult() | Write-Output
        $errorText = $stderr.GetAwaiter().GetResult()
        if ($child.ExitCode -ne 0) { throw "GC trial failed: $errorText" }
    }
    finally {
        if (-not $child.HasExited) { $child.Kill($true); $child.WaitForExit() }
        $child.Dispose()
    }
    $hashes = Get-Content -LiteralPath (Join-Path $folder 'result-hashes.json') -Raw
    if ($hashes -cne $expected) { throw 'GC configuration changed output bytes.' }
    $row = Get-Content -LiteralPath (Join-Path $folder 'metrics.json') -Raw | ConvertFrom-Json
    if (-not $row.ServerGc) { throw 'The requested GC configuration did not take effect.' }
    $row | Add-Member -NotePropertyName Round -NotePropertyValue $round
    $row | Add-Member -NotePropertyName HeapLimit -NotePropertyValue $heaps
    $rows.Add($row)
    ConvertTo-Json -InputObject @($rows.ToArray()) -Depth 4 | Set-Content -LiteralPath (Join-Path $Output 'measurements.json')
}

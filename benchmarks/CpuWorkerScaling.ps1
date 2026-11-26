param(
    [Parameter(Mandatory)][string]$Template,
    [Parameter(Mandatory)][string]$Output,
    [string]$Executable = 'benchmarks/Sortopia.CpuBench/bin/Release/net10.0/Sortopia.CpuBench.exe'
)
$ErrorActionPreference = 'Stop'
$Template = (Resolve-Path -LiteralPath $Template).Path
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw 'Use a new output directory.' }
New-Item -ItemType Directory -Path $Output | Out-Null
$expected = $null
$rows = [Collections.Generic.List[object]]::new()
foreach ($trial in @(@(1,4), @(1,8), @(1,16), @(2,16), @(2,8), @(2,4))) {
    $round, $workers = $trial
    $folder = Join-Path $Output "round-$round-workers-$workers"
    & $Executable $Template $folder $workers
    if ($LASTEXITCODE -ne 0) { throw "Worker-count trial failed: $folder" }
    $hashes = Get-Content -LiteralPath (Join-Path $folder 'result-hashes.json') -Raw
    if ($null -eq $expected) { $expected = $hashes }
    elseif ($hashes -cne $expected) { throw 'Worker count changed output bytes.' }
    $row = Get-Content -LiteralPath (Join-Path $folder 'metrics.json') -Raw | ConvertFrom-Json
    $row | Add-Member -NotePropertyName Round -NotePropertyValue $round
    $rows.Add($row)
    ConvertTo-Json -InputObject @($rows.ToArray()) -Depth 4 | Set-Content -LiteralPath (Join-Path $Output 'measurements.json')
}

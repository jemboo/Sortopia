param([Parameter(Mandatory)][string]$Measurements, [Parameter(Mandatory)][string]$Output)
$ErrorActionPreference = 'Stop'
$rows = @(Get-Content -LiteralPath $Measurements -Raw | ConvertFrom-Json)
function Median($values) {
    $ordered = @($values | Sort-Object)
    $middle = [int][Math]::Floor($ordered.Count / 2)
    if ($ordered.Count % 2) { $ordered[$middle] }
    else { ($ordered[$middle - 1] + $ordered[$middle]) / 2 }
}
$summary = foreach ($width in @($rows.Width | Sort-Object -Unique)) {
    $before = @($rows | Where-Object { $_.Width -eq $width -and $_.Build -eq 'baseline' })
    $after = @($rows | Where-Object { $_.Width -eq $width -and $_.Build -eq 'candidate' })
    if ($before.Count -eq 0 -or $before.Count -ne $after.Count) { throw "Incomplete pairs at width $width." }
    $baselineSeconds = Median $before.Seconds
    $candidateSeconds = Median $after.Seconds
    $baselinePeak = Median $before.PeakCommittedBytes
    $candidatePeak = Median $after.PeakCommittedBytes
    [pscustomobject]@{
        Width = $width; TestCount = $before[0].TestCount; TrialsPerBuild = $before.Count
        MembersPerPool = $before[0].MembersPerPool; Pools = $before[0].Pools; Generations = $before[0].Generations
        Workers = $before[0].Workers; Mode = $before[0].Mode
        BaselineSeconds = $baselineSeconds; CandidateSeconds = $candidateSeconds
        RuntimeReductionPercent = 100 * (1 - $candidateSeconds / $baselineSeconds)
        BaselineSecondsRange = @(($before.Seconds | Measure-Object -Minimum).Minimum, ($before.Seconds | Measure-Object -Maximum).Maximum)
        CandidateSecondsRange = @(($after.Seconds | Measure-Object -Minimum).Minimum, ($after.Seconds | Measure-Object -Maximum).Maximum)
        BaselineCpuSeconds = Median $before.CpuSeconds; CandidateCpuSeconds = Median $after.CpuSeconds
        BaselinePreparationSeconds = Median $before.PreparationSeconds; CandidatePreparationSeconds = Median $after.PreparationSeconds
        BaselinePeakCommittedMiB = $baselinePeak / 1MB; CandidatePeakCommittedMiB = $candidatePeak / 1MB
        PeakCommittedReductionPercent = 100 * (1 - $candidatePeak / $baselinePeak)
        BaselineMaxPeakCommittedMiB = ($before.PeakCommittedBytes | Measure-Object -Maximum).Maximum / 1MB
        CandidateMaxPeakCommittedMiB = ($after.PeakCommittedBytes | Measure-Object -Maximum).Maximum / 1MB
        BaselinePeakWorkingMiB = (Median $before.PeakWorkingSetBytes) / 1MB
        CandidatePeakWorkingMiB = (Median $after.PeakWorkingSetBytes) / 1MB
        BaselineAllocatedGiB = (Median $before.AllocatedBytes) / 1GB; CandidateAllocatedGiB = (Median $after.AllocatedBytes) / 1GB
        FilesPerRun = $before[0].Files.Count
    }
}
ConvertTo-Json -InputObject @($summary) -Depth 10 | Set-Content -LiteralPath $Output
$summary | Format-Table Width, TestCount, BaselineSeconds, CandidateSeconds, BaselinePeakCommittedMiB, CandidatePeakCommittedMiB

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
    $expectedCount = @($rows | Where-Object { $_.Width -eq $width -and $_.Build -eq 'baseline' }).Count
    foreach ($build in @('baseline', 'v1', 'optimized')) {
        $trials = @($rows | Where-Object { $_.Width -eq $width -and $_.Build -eq $build })
        if ($expectedCount -eq 0 -or $trials.Count -ne $expectedCount) { throw "Incomplete trials at width $width." }
        [pscustomobject]@{
            Width = $width; Build = $build; Trials = $trials.Count; TestCount = $trials[0].TestCount
            MembersPerPool = $trials[0].MembersPerPool; Pools = $trials[0].Pools; Generations = $trials[0].Generations
            Seconds = Median $trials.Seconds
            SecondsRange = @(($trials.Seconds | Measure-Object -Minimum).Minimum, ($trials.Seconds | Measure-Object -Maximum).Maximum)
            CpuSeconds = Median $trials.CpuSeconds
            PreparationSeconds = Median $trials.PreparationSeconds
            PeakResidentMiB = (Median $trials.PeakWorkingSetBytes) / 1MB
            PeakResidentRangeMiB = @((($trials.PeakWorkingSetBytes | Measure-Object -Minimum).Minimum / 1MB), (($trials.PeakWorkingSetBytes | Measure-Object -Maximum).Maximum / 1MB))
            PeakCommittedMiB = (Median $trials.PeakCommittedBytes) / 1MB
            PeakCommittedRangeMiB = @((($trials.PeakCommittedBytes | Measure-Object -Minimum).Minimum / 1MB), (($trials.PeakCommittedBytes | Measure-Object -Maximum).Maximum / 1MB))
            AllocatedGiB = (Median $trials.AllocatedBytes) / 1GB
            AllocatedRangeGiB = @((($trials.AllocatedBytes | Measure-Object -Minimum).Minimum / 1GB), (($trials.AllocatedBytes | Measure-Object -Maximum).Maximum / 1GB))
            SnapshotMiB = (Median $trials.SnapshotBytes) / 1MB
        }
    }
}
ConvertTo-Json -InputObject @($summary) -Depth 10 | Set-Content -LiteralPath $Output
$summary | Format-Table Width, Build, Seconds, PeakResidentMiB, PeakCommittedMiB, AllocatedGiB, SnapshotMiB

param([Parameter(Mandatory)][string]$Measurements, [Parameter(Mandatory)][string]$Output)
$ErrorActionPreference = 'Stop'
$rows = @(Get-Content -LiteralPath $Measurements -Raw | ConvertFrom-Json)
function Median($values) {
    $ordered = @($values | Sort-Object)
    $middle = [int][Math]::Floor($ordered.Count / 2)
    if ($ordered.Count % 2) { $ordered[$middle] }
    else { ($ordered[$middle-1] + $ordered[$middle]) / 2 }
}
function Range($values, $scale=1) {
    @((($values | Measure-Object -Minimum).Minimum / $scale), (($values | Measure-Object -Maximum).Maximum / $scale))
}
$summary = foreach ($width in @($rows.Width | Sort-Object -Unique)) {
    foreach ($population in @($rows.MembersPerPool | Sort-Object -Unique)) {
        $before = @($rows | Where-Object { $_.Width -eq $width -and $_.MembersPerPool -eq $population -and $_.Build -eq 'baseline' })
        $after = @($rows | Where-Object { $_.Width -eq $width -and $_.MembersPerPool -eq $population -and $_.Build -eq 'optimized' })
        if ($before.Count -eq 0 -or $before.Count -ne $after.Count) { throw "Incomplete case: $width/$population" }
        $baselineSeconds = Median $before.Seconds
        $candidateSeconds = Median $after.Seconds
        $baselineResident = Median $before.PeakWorkingSetBytes
        $candidateResident = Median $after.PeakWorkingSetBytes
        [pscustomobject]@{
            Width=$width; MembersPerPool=$population; Pools=$before[0].Pools; TestCount=$before[0].TestCount
            TrialsPerBuild=$before.Count; Workers=$before[0].Workers; CpuPercent=$before[0].CpuPercent
            BaselineSeconds=$baselineSeconds; CandidateSeconds=$candidateSeconds
            RuntimeReductionPercent=100*(1-$candidateSeconds/$baselineSeconds)
            BaselineSecondsRange=Range $before.Seconds; CandidateSecondsRange=Range $after.Seconds
            BaselinePeakResidentMiB=$baselineResident/1MB; CandidatePeakResidentMiB=$candidateResident/1MB
            ResidentReductionPercent=100*(1-$candidateResident/$baselineResident)
            BaselineResidentRangeMiB=Range $before.PeakWorkingSetBytes 1MB; CandidateResidentRangeMiB=Range $after.PeakWorkingSetBytes 1MB
            BaselinePeakCommittedMiB=(Median $before.PeakCommittedBytes)/1MB; CandidatePeakCommittedMiB=(Median $after.PeakCommittedBytes)/1MB
            BaselineCommittedRangeMiB=Range $before.PeakCommittedBytes 1MB; CandidateCommittedRangeMiB=Range $after.PeakCommittedBytes 1MB
            BaselineCpuSeconds=Median $before.CpuSeconds; CandidateCpuSeconds=Median $after.CpuSeconds
            BaselineAllocatedGiB=(Median $before.AllocatedBytes)/1GB; CandidateAllocatedGiB=(Median $after.AllocatedBytes)/1GB
            BaselineSnapshotMiB=(Median $before.SnapshotBytes)/1MB; CandidateSnapshotMiB=(Median $after.SnapshotBytes)/1MB
            CheckpointReductionPercent=100*(1-(Median $after.SnapshotBytes)/(Median $before.SnapshotBytes))
        }
    }
}
ConvertTo-Json -InputObject @($summary) -Depth 10 | Set-Content -LiteralPath $Output
$summary | Format-Table Width,MembersPerPool,BaselineSeconds,CandidateSeconds,BaselinePeakResidentMiB,CandidatePeakResidentMiB

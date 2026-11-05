$revisionRoot = 'Docs/Art/PlayerMotionV2/IdleThrowRevision/Preview'
$chargeGrips = @(@(1.1149,0.1767),@(1.0975,0.1413),@(1.0975,0.1413),@(1.0801,0.1695),@(1.0801,0.1695),@(1.0801,0.1695),@(1.1323,0.2201),@(1.1323,0.2201))
$anchorData = Get-Content -Raw -LiteralPath 'Docs/Art/PlayerMotionV2/IdleThrowRevision/anchors.json' | ConvertFrom-Json
$verified = @{ ThrowUpIdle=$anchorData.throwUp; ThrowUpRightIdle=$anchorData.throwUpRight; ThrowDownIdle=$anchorData.throwDown; ThrowDownRightIdle=$anchorData.throwDownRight; ThrowForwardAirIdle=$anchorData.throwForwardAir }
foreach ($sheetName in @('ChargeIdleStable','ThrowUpIdle','ThrowUpRightIdle','ThrowDownIdle','ThrowDownRightIdle','ThrowForwardAirIdle')) {
    $rows = @(Import-Csv -LiteralPath "$revisionRoot/$sheetName.csv")
    $columns = [Math]::Min(4,$rows.Count)
    $filters = @('scale=iw*3:ih*3:flags=neighbor')
    foreach ($row in $rows) {
        $i = [int]$row.index
        $nativeX = 96 + [double]$row.chainX * 32
        $nativeY = 96 - [double]$row.chainY * 32
        if ($verified.ContainsKey($sheetName)) { $nativeX = $verified[$sheetName][$i][0]; $nativeY = $verified[$sheetName][$i][1] }
        if ($sheetName -eq 'ChargeIdleStable' -and $i -eq 3) { $nativeX = $anchorData.chargeFrame3[0]; $nativeY = $anchorData.chargeFrame3[1] }
        $x = [int]([Math]::Round(($i % $columns * 192 + $nativeX) * 3))
        $y = [int]([Math]::Round(([Math]::Floor($i / $columns) * 192 + $nativeY) * 3))
        $filters += "drawbox=x=$($x-4):y=$($y-4):w=9:h=9:color=cyan:t=fill"
        if ($sheetName -eq 'ChargeIdleStable') {
            $gripX = [int]([Math]::Round(($i % $columns * 192 + 96 + $chargeGrips[$i][0] * 32) * 3))
            $gripY = [int]([Math]::Round(([Math]::Floor($i / $columns) * 192 + 96 - $chargeGrips[$i][1] * 32) * 3))
            $filters += "drawbox=x=$($gripX-3):y=$($gripY-3):w=7:h=7:color=yellow:t=fill"
        }
    }
    & ffmpeg -hide_banner -loglevel error -y -i "$revisionRoot/$sheetName.png" -vf ($filters -join ',') -frames:v 1 "$revisionRoot/$sheetName-anchors.png"
    if ($LASTEXITCODE -ne 0) { throw "Could not annotate $sheetName" }
}

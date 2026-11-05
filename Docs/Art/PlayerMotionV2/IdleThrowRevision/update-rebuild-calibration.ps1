$revisionRoot = 'Docs/Art/PlayerMotionV2/IdleThrowRevision'
$calibrationPath = 'Docs/Art/PlayerMotionV2/ArtRefinement/calibration.json'
$calibration = Get-Content -Raw -LiteralPath $calibrationPath | ConvertFrom-Json
$anchors = Get-Content -Raw -LiteralPath "$revisionRoot/anchors.json" | ConvertFrom-Json
$changes = @(
    @{ name='ThrowDownRefined'; source='Native_ThrowDown.png'; columns=4; rows=2; points=$anchors.throwDown },
    @{ name='ThrowDownRightRefined'; source='Native_ThrowDownRight.png'; columns=4; rows=2; points=$anchors.throwDownRight },
    @{ name='ThrowUpRightAirRefined'; source='Native_ThrowUpRightAir.png'; columns=3; rows=1; points=@($anchors.throwUpRight[3],$anchors.throwUpRight[4],$anchors.throwUpRight[5]) }
)
foreach ($change in $changes) {
    $sheet = @($calibration.sheets | Where-Object name -eq $change.name)
    if ($sheet.Count -ne 1) { throw "Expected one existing sheet named $($change.name)" }
    $sheet = $sheet[0]
    $sheet.source = "$revisionRoot/Sources/$($change.source)"
    $sheet.sourceColumns = $change.columns
    $sheet.sourceRows = $change.rows
    $sheet.poses = @(
        for ($i=0; $i -lt $change.points.Count; $i++) {
            [ordered]@{
                sourceIndex=$i; headX=103; headY=73; skullHeight=31
                targetX=103; targetY=73; targetSkull=31
                grounded=$false; bodyStretch=1; importScaleCompensation=1
                hasVisualAnchor=$true
                visualAnchorX=($change.points[$i][0]-96)/32
                visualAnchorY=(96-$change.points[$i][1])/32
            }
        }
    )
}
$calibration | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 -LiteralPath $calibrationPath

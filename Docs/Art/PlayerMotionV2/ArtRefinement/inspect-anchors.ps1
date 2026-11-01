$ErrorActionPreference = 'Stop'
$motionRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../../..')).Path
foreach ($motionName in @('RunRefined','FallRefined','ThrowDownRefined','ThrowDownRightRefined','ThrowUpRightAirRefined')) {
    $motionMetrics = Get-Content -LiteralPath (Join-Path $motionRoot ("Docs/Art/PlayerMotionV2/Implementation/" + $motionName + '-frames.txt'))
    $motionFilters = @('scale=iw*3:ih*3:flags=neighbor')
    for ($motionIndex = 0; $motionIndex -lt $motionMetrics.Count; $motionIndex++) {
        $motionMatch = [regex]::Match($motionMetrics[$motionIndex], 'chain=\((-?[0-9.]+),\s*(-?[0-9.]+)\)')
        if (!$motionMatch.Success) { throw ('Invalid metrics: ' + $motionMetrics[$motionIndex]) }
        $motionX = [double]::Parse($motionMatch.Groups[1].Value,[Globalization.CultureInfo]::InvariantCulture)
        $motionY = [double]::Parse($motionMatch.Groups[2].Value,[Globalization.CultureInfo]::InvariantCulture)
        $motionLeft = [int][Math]::Round(($motionIndex % 4 * 192 + 96 + $motionX * 32) * 3) - 6
        $motionTop = [int][Math]::Round(([Math]::Floor($motionIndex / 4) * 192 + 96 - $motionY * 32) * 3) - 6
        $motionFilters += "drawbox=x=$($motionLeft):y=$($motionTop):w=13:h=13:color=cyan:t=2"
    }
    $motionInput = Join-Path $motionRoot ("Assets/Art/Player/MotionV2/" + $motionName + '.png')
    $motionOutput = Join-Path $PSScriptRoot ($motionName + '-anchors.png')
    & ffmpeg -hide_banner -loglevel error -y -i $motionInput -vf ($motionFilters -join ',') -frames:v 1 $motionOutput
    if ($LASTEXITCODE -ne 0) {throw ('Anchor review failed: ' + $motionName)}
}

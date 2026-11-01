$ErrorActionPreference = 'Stop'
$motionRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../../..')).Path
$motionNative = Join-Path $motionRoot 'Assets/Art/Player/MotionV2'
$motionIdle = Join-Path $motionRoot 'Docs/Art/PlayerMotionV2/idle-reference.png'

# Sprite再生の比較資料。ゲームの物理や状態遷移を再現する動画ではない。
$motionCases = @(
    @{ Name='run'; Sheet='Run'; Before='mod(floor(t*9.4117647),8)'; After='mod(floor(t*7.0588235),6)' },
    @{ Name='charge'; Sheet='Charge'; Before='mod(floor(t*16),8)'; After='mod(floor(t*16),8)' },
    @{ Name='charge-run'; Sheet='ChargeRun'; Before='mod(floor(t*16),8)'; After='mod(floor(t*4.5882353),6)' },
    @{ Name='fall'; Sheet='Air'; Before='3+mod(floor(t*8),2)'; After='3+min(floor(t*5.5555556),1)' }
)

foreach ($motionCase in $motionCases) {
    $motionOld = Join-Path $PSScriptRoot ('Before/' + $motionCase.Sheet + '.png')
    $motionNew = Join-Path $motionNative ($motionCase.Sheet + '.png')
    $motionOutput = Join-Path $PSScriptRoot ('compare-' + $motionCase.Name + '.mp4')
    $motionFilter = "color=c=0x24242f:s=1152x448:r=24:d=4[base];" +
        "[0:v]crop=192:192:x='mod(" + $motionCase.Before + ",4)*192':y='floor((" + $motionCase.Before + ")/4)*192',scale=384:384:flags=neighbor[old];" +
        "[1:v]crop=192:192:x='mod(" + $motionCase.After + ",4)*192':y='floor((" + $motionCase.After + ")/4)*192',scale=384:384:flags=neighbor[new];" +
        "[2:v]crop=128:128:0:0,pad=192:192:32:32:color=black@0,scale=384:384:flags=neighbor[idle];" +
        "[base][idle]overlay=0:48[a];[a][old]overlay=384:48[b];[b][new]overlay=768:48," +
        "drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':text='IDLE':x=152:y=12:fontsize=22:fontcolor=white," +
        "drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':text='BEFORE':x=522:y=12:fontsize=22:fontcolor=white," +
        "drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':text='AFTER':x=916:y=12:fontsize=22:fontcolor=white[out]"
    & ffmpeg -hide_banner -loglevel error -y -loop 1 -framerate 24 -i $motionOld -loop 1 -framerate 24 -i $motionNew -loop 1 -framerate 24 -i $motionIdle -filter_complex $motionFilter -map '[out]' -t 4 -r 24 -an -c:v libx264 -crf 15 -preset fast -pix_fmt yuv420p $motionOutput
    if ($LASTEXITCODE -ne 0) { throw ('Could not render comparison: ' + $motionCase.Name) }
}

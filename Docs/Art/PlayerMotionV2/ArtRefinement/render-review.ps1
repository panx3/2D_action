$ErrorActionPreference = 'Stop'
$motionRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../../..')).Path
$motionNative = Join-Path $motionRoot 'Assets/Art/Player/MotionV2'
$motionIdle = Join-Path $motionRoot 'Docs/Art/PlayerMotionV2/idle-reference.png'
$motionCases = @(
    @{ Name='run'; Old='Run'; New='RunRefined'; OldFrames=@(0,2); NewFrames=@(0,2) },
    @{ Name='fall'; Old='Air'; New='FallRefined'; OldFrames=@(3,4); NewFrames=@(0,1) },
    @{ Name='down'; Old='ThrowDown'; New='ThrowDownRefined'; OldFrames=@(1,4); NewFrames=@(1,4) },
    @{ Name='down-right'; Old='ThrowDownRight'; New='ThrowDownRightRefined'; OldFrames=@(1,4); NewFrames=@(1,4) },
    @{ Name='up-right-air'; Old='ThrowUpRight'; New='ThrowUpRightAirRefined'; OldFrames=@(4,5); NewFrames=@(1,2) }
)

function MotionCrop([int]$index) {
    $motionX = ($index % 4) * 192
    $motionY = [math]::Floor($index / 4) * 192
    return "crop=192:192:${motionX}:${motionY},scale=384:384:flags=neighbor"
}
foreach ($motionCase in $motionCases) {
    $motionOld = Join-Path $PSScriptRoot ('Before/' + $motionCase.Old + '.png')
    $motionNew = Join-Path $motionNative ($motionCase.New + '.png')
    $motionOutput = Join-Path $PSScriptRoot ('review-' + $motionCase.Name + '.png')
    $motionFilter = "color=c=0x24242f:s=1920x448[base];" +
        "[0:v]crop=128:128:0:0,pad=192:192:32:32:color=black@0,scale=384:384:flags=neighbor[idle];" +
        "[1:v]split=2[o0][o1];[2:v]split=2[n0][n1];" +
        "[o0]$(MotionCrop $motionCase.OldFrames[0])[old0];[n0]$(MotionCrop $motionCase.NewFrames[0])[new0];" +
        "[o1]$(MotionCrop $motionCase.OldFrames[1])[old1];[n1]$(MotionCrop $motionCase.NewFrames[1])[new1];" +
        "[base][idle]overlay=0:48[a];[a][old0]overlay=384:48[b];[b][new0]overlay=768:48[c];" +
        "[c][old1]overlay=1152:48[d];[d][new1]overlay=1536:48," +
        "drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':text='IDLE':x=152:y=12:fontsize=22:fontcolor=white," +
        "drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':text='BEFORE':x=516:y=12:fontsize=22:fontcolor=white," +
        "drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':text='AFTER':x=915:y=12:fontsize=22:fontcolor=white," +
        "drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':text='BEFORE':x=1284:y=12:fontsize=22:fontcolor=white," +
        "drawtext=fontfile='C\:/Windows/Fonts/arial.ttf':text='AFTER':x=1683:y=12:fontsize=22:fontcolor=white[out]"
    & ffmpeg -hide_banner -loglevel error -y -i $motionIdle -i $motionOld -i $motionNew -filter_complex $motionFilter -map '[out]' -frames:v 1 -update 1 $motionOutput
    if ($LASTEXITCODE -ne 0) { throw ('Could not render review: ' + $motionCase.Name) }
}

# These comparisons show sprite timing, not a simulation of gameplay or the chain.
$motionVideos = @(
    @{ Name='run'; Old='Run'; New='RunRefined'; Before='mod(floor(t*7.0588235),6)'; After='mod(floor(t*7.0588235),6)' },
    @{ Name='fall'; Old='Air'; New='FallRefined'; Before='3+min(floor(t*5.5555556),1)'; After='min(floor(t*5.5555556),1)' },
    @{ Name='down'; Old='ThrowDown'; New='ThrowDownRefined'; Before='min(floor(t*16.6666667),2)'; After='min(floor(t*16.6666667),2)' },
    @{ Name='down-right'; Old='ThrowDownRight'; New='ThrowDownRightRefined'; Before='3+min(floor(t*16.6666667),2)'; After='3+min(floor(t*16.6666667),2)' },
    @{ Name='up-right-air'; Old='ThrowUpRight'; New='ThrowUpRightAirRefined'; Before='3+min(floor(t*16.6666667),2)'; After='min(floor(t*16.6666667),2)' },
    @{ Name='charge'; Old='Charge'; New='Charge'; Before='mod(floor(t*16),8)'; After='if(lt(mod(floor(t*16),8),2),0,if(lt(mod(floor(t*16),8),4),1,if(lt(mod(floor(t*16),8),6),3,7)))' }
)
foreach ($motionCase in $motionVideos) {
    $motionOld = Join-Path $PSScriptRoot ('Before/' + $motionCase.Old + '.png')
    $motionNew = Join-Path $motionNative ($motionCase.New + '.png')
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

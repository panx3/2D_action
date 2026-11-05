$root = 'Docs/Art/PlayerMotionV2/IdleThrowRevision'
$spriteRoot = 'Assets/Art/Player/MotionV2'
$filter = '[0:v]scale=128:128:flags=neighbor,pad=192:192:32:32:color=black,split=2[i0][i1];' +
    '[1:v]crop=192:192:192:0[g0];[2:v]crop=192:192:192:0[g1];[7:v]crop=192:192:192:0[a1];' +
    '[3:v]split=2[c][d];[c]crop=192:192:192:0[g2];[d]crop=192:192:0:192[a2];' +
    '[4:v]split=2[e][f];[e]crop=192:192:192:0[g3];[f]crop=192:192:0:192[a3];' +
    '[5:v]split=2[g][h];[g]crop=192:192:192:0[g4];[h]crop=192:192:0:192[a4];' +
    '[6:v]crop=192:192:192:0[a0];' +
    '[i0][g0][g1][g2][g3][g4][i1][a0][a1][a2][a3][a4]' +
    'xstack=inputs=12:layout=0_0|192_0|384_0|576_0|768_0|960_0|0_192|192_192|384_192|576_192|768_192|960_192:fill=black,' +
    'scale=iw*2:ih*2:flags=neighbor'
& ffmpeg -hide_banner -loglevel error -y -i "$root/Idle-reference-6x.png" `
    -i "$spriteRoot/ThrowForward.png" -i "$spriteRoot/ThrowUpRight.png" -i "$spriteRoot/ThrowUp.png" `
    -i "$spriteRoot/ThrowDownRightRefined.png" -i "$spriteRoot/ThrowDownRefined.png" `
    -i "$spriteRoot/ThrowForwardAirIdle.png" -i "$spriteRoot/ThrowUpRightAirRefined.png" `
    -filter_complex $filter -frames:v 1 "$root/Preview/Comparison-final.png"
if ($LASTEXITCODE -ne 0) { throw 'Could not render comparison.' }

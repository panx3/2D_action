param([string]$Original,[string]$Output)
$ErrorActionPreference='Stop'
function Blocks([string]$text){$map=@{};foreach($m in [regex]::Matches($text,'(?ms)^--- !u!\d+ &(?<id>\d+)[^\n]*\n.*?(?=^--- !u!|\z)')){$map[$m.Groups['id'].Value]=$m.Value};return $map}
$root=Join-Path $pwd '.codex-jewel-ui-work'
$before=Blocks ([IO.File]::ReadAllText((Join-Path $root 'scene-before.unity')).Replace("`r`n","`n"))
$after=Blocks ([IO.File]::ReadAllText((Join-Path $root 'scene-after.unity')).Replace("`r`n","`n"))
$text=[IO.File]::ReadAllText($Original).Replace("`r`n","`n");$originalBlocks=Blocks $text
$changed=0
foreach($id in $after.Keys){
 if(-not $before.ContainsKey($id)){throw "Unexpected new Scene record: $id"}
 if($before[$id] -ne $after[$id]){
  if($originalBlocks[$id] -ne $before[$id]){throw "Preserved Scene record differs: $id"}
  $text=$text.Replace($originalBlocks[$id],$after[$id]);$changed++
 }
}
if($changed -ne 2){throw "Expected only two UI instance records, found $changed"}
[IO.File]::WriteAllText($Output,$text,[Text.UTF8Encoding]::new($false))
Write-Output ('Merged '+$changed+' Unity-generated UI records; all other Scene bytes retained')

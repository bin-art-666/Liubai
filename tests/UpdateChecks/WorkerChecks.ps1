$ErrorActionPreference='Stop'
$workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $workspace 'src/Liubai/Assets/UpdateWorker.ps1')
$script:checks=0; $script:restores=0; $script:kind='ready'; $script:launched=0
function Check($Value,$Name){if(-not $Value){throw $Name};$script:checks++}
function Wait-UpdateParent($Plan){}
function Suspend-UpdateStartup {return 'fixture-task'}
function Restore-UpdateStartup($Name){if($Name -eq 'fixture-task'){$script:restores++}}
function Wait-UpdatedRuntime($Child,$Ready){}
function Start-UpdatedRuntime($Plan,$ReadyId) {
 $script:launched++
 if($script:kind -eq 'throw' -and -not $ReadyId.StartsWith('recovery-')){throw 'Injected launch failure'}
 if($script:kind -eq 'exited' -and -not $ReadyId.StartsWith('recovery-')){return [pscustomobject]@{Id=99;HasExited=$true}}
 if($script:kind -ne 'unconfirmed') {Write-UpdateJson (Join-Path $script:currentRoot ('ready-'+$ReadyId+'.json')) @{id=$ReadyId;pid=99}}
 return [pscustomobject]@{Id=99;HasExited=$false}
}
function Runtime($Path,$Label) {
 New-Item -ItemType Directory -Path $Path -Force | Out-Null
 foreach($name in @('Liubai.exe','Liubai.dll','Liubai.runtimeconfig.json','Liubai.deps.json','resources.pri','App.xbf')){[IO.File]::WriteAllText((Join-Path $Path $name),($Label+'-'+$name))}
}
function Fixture($Name) {
 $base=Join-Path $script:testRoot $Name; $install=Join-Path $base 'app'; Runtime $install 'old'
 $sha=[Security.Cryptography.SHA256]::Create()
 try{$key=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($install.ToUpperInvariant())))).Replace('-','').Substring(0,12).ToLowerInvariant()}finally{$sha.Dispose()}
 $root=Join-Path $base ('.liubai-update-'+$key);New-Item -ItemType Directory -Path $root | Out-Null
 Write-UpdateJson (Join-Path $root 'owner.json') $install
 $data=Join-Path $base 'data';New-Item -ItemType Directory -Path $data | Out-Null;[IO.File]::WriteAllText((Join-Path $data 'planner.json'),'new user records')
 $script:currentRoot=$root
 return @{root=$root;install=$install;data=$data}
}
function Plan($Fixture,$Mode='update') {
 $id=[guid]::NewGuid().ToString('N');$stage=if($Mode -eq 'rollback'){Join-Path $Fixture.root 'previous'}else{Join-Path $Fixture.root ('stage-'+$id)}
 if($Mode -eq 'update'){Runtime $stage 'new'}
 $hashes=@{};foreach($file in Get-ChildItem -LiteralPath $stage -File){$hashes[$file.Name]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
 $plan=@{Version=1;Id=$id;Mode=$Mode;InstallRoot=$Fixture.install;DataRoot=$Fixture.data;Stage=$stage;ParentPid=$PID;ParentStart='0';ExeSha256=$hashes['Liubai.exe'];TargetVersion='0.7.0';SourceVersion='0.6.0';Isolated=$true;Hashes=$hashes}
 $path=Join-Path $Fixture.root ('plan-'+$id+'.json');Write-UpdateJson $path $plan;Write-UpdateJson (Join-Path $Fixture.root ('commit-'+$id+'.json')) @{id=$id}
 $settings=Join-Path $Fixture.root ('settings-'+$id);New-Item -ItemType Directory -Path $settings | Out-Null;[IO.File]::WriteAllText((Join-Path $settings 'planner.json'),'snapshot records')
 return @{path=$path;plan=$plan}
}
function Result($Fixture){Get-Content -LiteralPath (Join-Path $Fixture.root 'result.json') -Raw -Encoding UTF8 | ConvertFrom-Json}
$script:testRoot=Join-Path $workspace ('.build/update-worker-checks-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $script:testRoot | Out-Null
try {
 $f=Fixture 'success';$p=Plan $f;Invoke-LiubaiUpdate $p.path
 Check ((Result $f).state -eq 'success') 'Successful transaction'
 Check ((Get-Content -LiteralPath (Join-Path $f.install 'Liubai.dll') -Raw) -eq 'new-Liubai.dll') 'New runtime installed'
 Check ((Get-Content -LiteralPath (Join-Path $f.root 'previous/Liubai.dll') -Raw) -eq 'old-Liubai.dll') 'Previous runtime retained'
 Check ((Get-Content -LiteralPath (Join-Path $f.data 'planner.json') -Raw) -eq 'new user records') 'Current records retained'
 Check (Test-Path -LiteralPath (Join-Path $f.root 'previous-settings/planner.json')) 'Snapshot retained'
 Check ($script:restores -eq 1) 'Startup restored after success'
 $p=Plan $f 'rollback';Invoke-LiubaiUpdate $p.path
 Check ((Result $f).state -eq 'success') 'Rollback succeeds'
 Check ((Get-Content -LiteralPath (Join-Path $f.install 'Liubai.dll') -Raw) -eq 'old-Liubai.dll') 'Old version restored'
 Check ((Get-Content -LiteralPath (Join-Path $f.root 'previous/Liubai.dll') -Raw) -eq 'new-Liubai.dll') 'Reverse rollback copy retained'
 Check ((Get-Content -LiteralPath (Join-Path $f.data 'planner.json') -Raw) -eq 'new user records') 'Rollback preserves recent records'
 foreach($kind in @('throw','exited')) {
  $script:kind=$kind;$f=Fixture $kind;Runtime (Join-Path $f.root 'previous') 'older';$p=Plan $f;Invoke-LiubaiUpdate $p.path
  Check ((Result $f).state -eq 'failed' -and (Result $f).restored) 'Launch failure auto-restores runtime'
  Check ((Get-Content -LiteralPath (Join-Path $f.install 'Liubai.dll') -Raw) -eq 'old-Liubai.dll') 'Working runtime restored'
  Check ((Get-Content -LiteralPath (Join-Path $f.root 'previous/Liubai.dll') -Raw) -eq 'older-Liubai.dll') 'Earlier rollback copy preserved on failure'
  Check (-not(Test-Path -LiteralPath (Join-Path $f.root 'failed'))) 'Failed package cleaned'
 }
 $script:kind='ready';$f=Fixture 'no-commit';$p=Plan $f;Remove-Item -LiteralPath (Join-Path $f.root ('commit-'+$p.plan.Id+'.json'));Invoke-LiubaiUpdate $p.path
 Check ((Result $f).state -eq 'failed' -and -not (Result $f).restored) 'No commit prevents replacement'
 Check ((Get-Content -LiteralPath (Join-Path $f.install 'Liubai.dll') -Raw) -eq 'old-Liubai.dll') 'No commit preserves runtime'
 $f=Fixture 'corrupt';$p=Plan $f;[IO.File]::WriteAllText((Join-Path $p.plan.Stage 'Liubai.dll'),'corrupted');Invoke-LiubaiUpdate $p.path
 Check ((Result $f).state -eq 'failed') 'Core corruption detected after parent exit'
 Check ((Get-Content -LiteralPath (Join-Path $f.install 'Liubai.dll') -Raw) -eq 'old-Liubai.dll') 'Corrupt stage leaves runtime untouched'
 $f=Fixture 'path';$p=Plan $f;$foreign=Join-Path $f.root 'foreign.json';Copy-Item -LiteralPath $p.path -Destination $foreign
 $rejected=$false;try{Invoke-LiubaiUpdate $foreign}catch{$rejected=$true};Check $rejected 'Foreign plan path rejected'
 $script:kind='unconfirmed';$f=Fixture 'unconfirmed';$p=Plan $f;Invoke-LiubaiUpdate $p.path
 Check ((Result $f).state -eq 'unconfirmed') 'Live unconfirmed process never killed or renamed'
 Check ((Get-Content -LiteralPath (Join-Path $f.install 'Liubai.dll') -Raw) -eq 'new-Liubai.dll') 'Live runtime kept in place'
 Check (Test-Path -LiteralPath (Join-Path $f.root 'previous.json')) 'Unconfirmed runtime retains accessible rollback metadata'
 Check ($script:restores -eq 7) 'Startup restored on every started transaction'
 Write-Output ('PASS: '+$script:checks+' update worker transaction, rollback, failure, commit and startup checks')
} finally {
 $absolute=[IO.Path]::GetFullPath($script:testRoot)
 $expected=Join-Path $workspace '.build'
 if(-not $absolute.StartsWith($expected+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe test cleanup path'}
 Remove-Item -LiteralPath $absolute -Recurse -Force
}

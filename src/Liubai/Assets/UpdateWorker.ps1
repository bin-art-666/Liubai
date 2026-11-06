param([string]$PlanPath)
$ErrorActionPreference='Stop'

function Write-UpdateJson($Path,$Value) {
 $temp=$Path+'.tmp'
 [IO.File]::WriteAllText($temp,($Value | ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
 Move-Item -LiteralPath $temp -Destination $Path -Force
}
function Assert-UpdateChild($Path,$Root) {
 $full=[IO.Path]::GetFullPath($Path)
 if(-not $full.StartsWith($Root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Path outside update directory'}
 if(Test-Path -LiteralPath $full) {
  $item=Get-Item -LiteralPath $full -Force
  if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked update directory'}
  if($item.PSIsContainer -and (Get-ChildItem -LiteralPath $full -Recurse -Force | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint} | Select-Object -First 1)){throw 'Linked update content'}
 }
}
function Remove-UpdateChild($Path,$Root) {
 Assert-UpdateChild $Path $Root
 if(Test-Path -LiteralPath $Path){Remove-Item -LiteralPath $Path -Recurse -Force}
}
function Wait-UpdateParent($Plan) {
 $parent=Get-Process -Id $Plan.ParentPid -ErrorAction SilentlyContinue
 if($parent) {
  if($parent.Path -ne (Join-Path $Plan.InstallRoot 'Liubai.exe') -or $parent.StartTime.ToUniversalTime().Ticks.ToString() -ne $Plan.ParentStart){throw 'Parent identity mismatch'}
  if(-not $parent.WaitForExit(60000)){throw 'Parent did not exit; no files replaced'}
 }
}
function Suspend-UpdateStartup {
 $name='Liubai-'+[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
 $task=Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
 if($task -and $task.State -ne 'Disabled') {Disable-ScheduledTask -TaskName $name | Out-Null; return $name}
 return $null
}
function Restore-UpdateStartup($Name) {if($Name){Enable-ScheduledTask -TaskName $Name | Out-Null}}
function Start-UpdatedRuntime($Plan,$ReadyId) {
 $arguments='--maintenance-ready '+$ReadyId
 if($Plan.Isolated){$arguments+=' --data-dir "'+$Plan.DataRoot+'"'}
 Start-Process -FilePath (Join-Path $Plan.InstallRoot 'Liubai.exe') -ArgumentList $arguments -WorkingDirectory $Plan.InstallRoot -WindowStyle Hidden -PassThru
}
function Wait-UpdatedRuntime($Child,$Ready) {
 $deadline=[DateTime]::UtcNow.AddSeconds(30)
 while(-not(Test-Path -LiteralPath $Ready) -and -not $Child.HasExited -and [DateTime]::UtcNow -lt $deadline){Start-Sleep -Milliseconds 200; $Child.Refresh()}
}
function Complete-UpdateBackup($Plan,$Root) {
 $previous=Join-Path $Root 'previous'; $retired=Join-Path $Root 'retired'
 if($Plan.Mode -eq 'rollback'){if(Test-Path -LiteralPath $retired){Move-Item -LiteralPath $retired -Destination $previous}}
 else {Remove-UpdateChild $retired $Root}
 $settings=Join-Path $Root ('settings-'+$Plan.Id); $oldSettings=Join-Path $Root 'previous-settings'
 if(Test-Path -LiteralPath $settings){Remove-UpdateChild $oldSettings $Root; Move-Item -LiteralPath $settings -Destination $oldSettings}
 $hashes=@{}
 foreach($name in @('Liubai.exe','Liubai.dll','Liubai.runtimeconfig.json','Liubai.deps.json','resources.pri','App.xbf')){$hashes[$name]=(Get-FileHash -LiteralPath (Join-Path $previous $name) -Algorithm SHA256).Hash}
 Write-UpdateJson (Join-Path $Root 'previous.json') @{version=$Plan.SourceVersion;mode=$Plan.Mode;exeSha256=$hashes['Liubai.exe'];hashes=$hashes}
}
function Invoke-LiubaiUpdate([string]$Path) {
 $plan=Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
 if($plan.Version -ne 1 -or $plan.Id -notmatch '^[a-f0-9]{32}$' -or $plan.Mode -notin @('update','rollback')){throw 'Invalid update plan'}
 $install=[IO.Path]::GetFullPath($plan.InstallRoot).TrimEnd('\')
 $parent=[IO.Path]::GetDirectoryName($install)
 $sha=[Security.Cryptography.SHA256]::Create()
 try {$key=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($install.ToUpperInvariant())))).Replace('-','').Substring(0,12).ToLowerInvariant()}finally{$sha.Dispose()}
 $root=Join-Path $parent ('.liubai-update-'+$key)
 if([IO.Path]::GetFullPath($Path) -ne (Join-Path $root ('plan-'+$plan.Id+'.json'))){throw 'Plan path mismatch'}
 if((Get-Content -LiteralPath (Join-Path $root 'owner.json') -Raw -Encoding UTF8 | ConvertFrom-Json) -ne $install){throw 'Update owner mismatch'}
 if((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked update root'}
 $previous=Join-Path $root 'previous'; $retired=Join-Path $root 'retired'; $failed=Join-Path $root 'failed'
 $settings=Join-Path $root ('settings-'+$plan.Id); $oldSettings=Join-Path $root 'previous-settings'
 foreach($target in @($Path,$plan.Stage,$previous,$retired,$failed,$settings,$oldSettings)){Assert-UpdateChild $target $root}
 if($plan.Mode -eq 'rollback' -and $plan.Stage -ne $previous){throw 'Rollback source mismatch'}
 if($plan.Mode -eq 'update' -and $plan.Stage -ne (Join-Path $root ('stage-'+$plan.Id))){throw 'Stage source mismatch'}
 foreach($dir in @($install,$plan.Stage)) {
  foreach($name in @('Liubai.exe','Liubai.dll','Liubai.runtimeconfig.json','Liubai.deps.json','resources.pri','App.xbf')){
   if(-not(Test-Path -LiteralPath (Join-Path $dir $name) -PathType Leaf)){throw 'Incomplete runtime'}
  }
 }
 if((Get-FileHash -LiteralPath (Join-Path $plan.Stage 'Liubai.exe') -Algorithm SHA256).Hash -ne $plan.ExeSha256){throw 'Staged executable hash mismatch'}
 $lock=[IO.File]::Open((Join-Path $root 'worker.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
 $task=$null; $moved=$false; $replaced=$false; $launched=$false
 $result=Join-Path $root 'result.json'; $ready=Join-Path $root ('ready-'+$plan.Id+'.json')
 try {
  $task=Suspend-UpdateStartup
  Write-UpdateJson (Join-Path $root ('started-'+$plan.Id+'.json')) @{pid=$PID;id=$plan.Id}
  Wait-UpdateParent $plan
  $commit=Join-Path $root ('commit-'+$plan.Id+'.json')
  if(-not(Test-Path -LiteralPath $commit) -or (Get-Content -LiteralPath $commit -Raw -Encoding UTF8 | ConvertFrom-Json).id -ne $plan.Id){throw 'Parent did not commit the update'}
  # Revalidate after the parent has flushed and exited.
  Assert-UpdateChild $plan.Stage $root
  foreach($property in $plan.Hashes.PSObject.Properties) {
   if($property.Name -notin @('Liubai.exe','Liubai.dll','Liubai.runtimeconfig.json','Liubai.deps.json','resources.pri','App.xbf')){throw 'Unexpected core manifest entry'}
   if((Get-FileHash -LiteralPath (Join-Path $plan.Stage $property.Name) -Algorithm SHA256).Hash -ne $property.Value){throw 'Staged core hash mismatch'}
  }
  if(@($plan.Hashes.PSObject.Properties).Count -ne 6){throw 'Incomplete core manifest'}
  if((Get-Item -LiteralPath $install -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked install directory'}
  if(Get-ChildItem -LiteralPath $install -Recurse -Force | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint} | Select-Object -First 1){throw 'Linked install content'}
  Write-UpdateJson $result @{state='applying';id=$plan.Id;mode=$plan.Mode;version=$plan.TargetVersion}
  Remove-UpdateChild $retired $root; Remove-UpdateChild $failed $root
  if($plan.Mode -eq 'update') {
   if(Test-Path -LiteralPath $previous){Move-Item -LiteralPath $previous -Destination $retired}
   Move-Item -LiteralPath $install -Destination $previous; $moved=$true
  } else {Move-Item -LiteralPath $install -Destination $retired; $moved=$true}
  Move-Item -LiteralPath $plan.Stage -Destination $install; $replaced=$true
  $child=Start-UpdatedRuntime $plan $plan.Id; $launched=$true
  Wait-UpdatedRuntime $child $ready
  if(-not(Test-Path -LiteralPath $ready)) {
   if(-not $child.HasExited) {Complete-UpdateBackup $plan $root; Write-UpdateJson $result @{state='unconfirmed';id=$plan.Id;mode=$plan.Mode;version=$plan.TargetVersion};return}
   throw 'New runtime exited before startup confirmation'
  }
  $confirmation=Get-Content -LiteralPath $ready -Raw -Encoding UTF8 | ConvertFrom-Json
  if($confirmation.id -ne $plan.Id -or $confirmation.pid -ne $child.Id){throw 'Startup confirmation mismatch'}
  Complete-UpdateBackup $plan $root
  Write-UpdateJson $result @{state='success';id=$plan.Id;mode=$plan.Mode;version=$plan.TargetVersion;time=[DateTimeOffset]::Now.ToString('O')}
 } catch {
  $message=$_.Exception.Message
  # Never rename a live, unconfirmed new runtime or forcibly terminate it.
  if($replaced -and $launched -and -not $child.HasExited){Write-UpdateJson $result @{state='unconfirmed';id=$plan.Id;error=$message};return}
  if($replaced){Move-Item -LiteralPath $install -Destination $failed}
  if($moved) {
   $restore=if($plan.Mode -eq 'update'){$previous}else{$retired}
   Move-Item -LiteralPath $restore -Destination $install
   if($plan.Mode -eq 'update' -and (Test-Path -LiteralPath $retired)){Move-Item -LiteralPath $retired -Destination $previous}
   try {$null=Start-UpdatedRuntime $plan ('recovery-'+$plan.Id)}catch{$message+='; restart failed: '+$_.Exception.Message}
   try {Remove-UpdateChild $failed $root}catch{$message+='; failed package cleanup pending'}
  }
  Write-UpdateJson $result @{state='failed';id=$plan.Id;error=$message;restored=$moved}
 } finally {
  try {Restore-UpdateStartup $task}finally{$lock.Dispose()}
 }
}
if($MyInvocation.InvocationName -ne '.') {Invoke-LiubaiUpdate $PlanPath}

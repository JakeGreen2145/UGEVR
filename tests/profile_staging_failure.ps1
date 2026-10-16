#requires -Version 7.0
param([Parameter(Mandatory)][string]$Controller)
$ErrorActionPreference='Stop'
$taskDll=[IO.Path]::GetFullPath($Controller)
$taskTemp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$taskRoot=[IO.Path]::Combine($taskTemp,'ugevr-profile-stage-'+[Guid]::NewGuid().ToString('N'))
if([IO.Path]::GetDirectoryName($taskRoot) -ne $taskTemp){throw 'Invalid temporary fixture root.'}
$taskSddl=$null
$taskParent=$null
$taskResult=@{prior_profile_unchanged=$false;acl_restored=$false;staged_files=-1}
try {
    $null=New-Item -ItemType Directory -Path $taskRoot
    $taskInitial=& dotnet $taskDll profile save --storage-root $taskRoot --game-id steam:383870 --scope personal --expected-revision 0 --setting Tracking.UnitsPerMeter --value 2
    if($LASTEXITCODE -ne 0){throw 'Initial fixture save failed.'}
    $taskDigest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('steam:383870'))).ToLowerInvariant()
    $taskParent=Join-Path $taskRoot ('games/'+$taskDigest+'/personal')
    $taskConfig=Join-Path $taskParent 'config.txt'
    if(-not(Test-Path -LiteralPath ($taskConfig+'.lock') -PathType Leaf)){throw 'Existing lock fixture absent.'}
    $taskBefore=(Get-FileHash -LiteralPath $taskConfig -Algorithm SHA256).Hash
    $taskSddl=(Get-Acl -LiteralPath $taskParent).Sddl
    $taskAcl=Get-Acl -LiteralPath $taskParent
    $taskSid=[Security.Principal.WindowsIdentity]::GetCurrent().User
    $taskRule=[Security.AccessControl.FileSystemAccessRule]::new($taskSid,[Security.AccessControl.FileSystemRights]::CreateFiles,[Security.AccessControl.InheritanceFlags]::None,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Deny)
    $taskAcl.AddAccessRule($taskRule)
    Set-Acl -LiteralPath $taskParent -AclObject $taskAcl
    $taskOutput=& dotnet $taskDll profile save --storage-root $taskRoot --game-id steam:383870 --scope personal --expected-revision 1 --setting Tracking.UnitsPerMeter --value 3
    $taskExit=$LASTEXITCODE
    $taskAfter=(Get-FileHash -LiteralPath $taskConfig -Algorithm SHA256).Hash
    $taskStages=@(Get-ChildItem -LiteralPath $taskParent -Filter '*.tmp')
    if($taskExit -ne 2 -or ($taskOutput -join "`n") -notmatch '\.tmp' -or $taskBefore -ne $taskAfter -or $taskStages.Count -ne 0){throw 'Staging failure did not preserve the prior profile.'}
    $taskResult.prior_profile_unchanged=$true
    $taskResult.staged_files=0
}
finally {
    if($null -ne $taskSddl){
        $taskRestore=Get-Acl -LiteralPath $taskParent
        $taskRestore.SetSecurityDescriptorSddlForm($taskSddl)
        Set-Acl -LiteralPath $taskParent -AclObject $taskRestore
        if((Get-Acl -LiteralPath $taskParent).Sddl -ne $taskSddl){throw 'Temporary ACL restoration was not exact.'}
        $taskResult.acl_restored=$true
    }
    $taskResolved=[IO.Path]::GetFullPath($taskRoot)
    if([IO.Path]::GetDirectoryName($taskResolved) -ne $taskTemp -or [IO.Path]::GetFileName($taskResolved) -notmatch '^ugevr-profile-stage-[a-f0-9]{32}$'){throw 'Unsafe fixture cleanup.'}
    if(Test-Path -LiteralPath $taskResolved){Remove-Item -LiteralPath $taskResolved -Recurse -Force}
}
$taskResult | ConvertTo-Json -Compress

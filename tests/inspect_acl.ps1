param([string]$Action,[string]$Root,[string]$Path,[string]$Snapshot)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
$taskPath=[IO.Path]::GetFullPath($Path)
$taskSnapshot=[IO.Path]::GetFullPath($Snapshot)
if(-not $taskPath.StartsWith($taskRoot,[StringComparison]::OrdinalIgnoreCase) -or -not $taskSnapshot.StartsWith($taskRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'ACL fixture path outside its temporary root.'}
if($Action -eq 'deny') {
    $taskOriginal=Get-Acl -LiteralPath $taskPath
    [IO.File]::WriteAllText($taskSnapshot,$taskOriginal.Sddl)
    $taskSid=[Security.Principal.WindowsIdentity]::GetCurrent().User
    $taskRule=[Security.AccessControl.FileSystemAccessRule]::new($taskSid,[Security.AccessControl.FileSystemRights]::ReadData,[Security.AccessControl.InheritanceFlags]::None,[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Deny)
    $taskOriginal.AddAccessRule($taskRule);Set-Acl -LiteralPath $taskPath -AclObject $taskOriginal
} elseif($Action -eq 'restore') {
    $taskSddl=[IO.File]::ReadAllText($taskSnapshot);$taskAcl=Get-Acl -LiteralPath $taskPath
    $taskAcl.SetSecurityDescriptorSddlForm($taskSddl);Set-Acl -LiteralPath $taskPath -AclObject $taskAcl
    if((Get-Acl -LiteralPath $taskPath).Sddl -ne $taskSddl){throw 'ACL fixture restoration not exact.'}
} else {throw 'Unknown ACL fixture action.'}

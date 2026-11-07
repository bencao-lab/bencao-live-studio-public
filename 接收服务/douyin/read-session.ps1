$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Security
$folder=if($env:BENCAO_DATA_ROOT){$env:BENCAO_DATA_ROOT}else{throw '必须由外发版接收服务启动'}
$path=Join-Path $folder 'douyin-session.bin'
$bytes=[Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($path),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
[Console]::Write([Text.Encoding]::UTF8.GetString($bytes))

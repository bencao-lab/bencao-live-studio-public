$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Security
if(!$env:BENCAO_DATA_ROOT){throw '缺少外发版配置目录'}
$folder=$env:BENCAO_DATA_ROOT
$cookie=[Console]::In.ReadToEnd()
if(!$cookie){throw '缺少登录状态'}
[void][IO.Directory]::CreateDirectory($folder)
$path=Join-Path $folder 'douyin-session.bin'
$data=[Security.Cryptography.ProtectedData]::Protect([Text.Encoding]::UTF8.GetBytes($cookie),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
$temp=$path+'.'+[Guid]::NewGuid().ToString()+'.tmp'
try{
    [IO.File]::WriteAllBytes($temp,$data)
    if([IO.File]::Exists($path)){[IO.File]::Replace($temp,$path,$null)}else{[IO.File]::Move($temp,$path)}
}finally{if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}

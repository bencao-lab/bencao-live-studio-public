$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$out=Join-Path $root '直播台'
[void][IO.Directory]::CreateDirectory($out)
$src=Join-Path $root '源码'
$sdk=Join-Path $root '厂商SDK/WebView2'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if((Get-AuthenticodeSignature $compiler).Status -ne 'Valid'){throw '无法验证微软编译器签名'}
foreach($name in 'Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll'){
    if((Get-AuthenticodeSignature (Join-Path $sdk $name)).Status -ne 'Valid'){throw "SDK 签名验证失败：$name"}
    $dest=Join-Path $out $name
    if(!(Test-Path $dest) -or (Get-FileHash $dest).Hash -ne (Get-FileHash (Join-Path $sdk $name)).Hash){Copy-Item (Join-Path $sdk $name) $out -Force}
}
$sources=@(Get-ChildItem $src -Filter '*.cs' | ForEach-Object FullName)
$outputFile=Join-Path $out '本草收藏数字人直播台.exe'
& $compiler /nologo /define:PUBLIC_RELEASE /target:winexe /platform:x64 "/out:$outputFile" /r:System.Net.Http.dll /r:System.Security.dll /r:System.Web.Extensions.dll "/r:$sdk/Microsoft.Web.WebView2.Core.dll" "/r:$sdk/Microsoft.Web.WebView2.WinForms.dll" $sources
if($LASTEXITCODE -ne 0){throw '编译失败'}
Get-FileHash -Algorithm SHA256 $outputFile |select Hash,Path

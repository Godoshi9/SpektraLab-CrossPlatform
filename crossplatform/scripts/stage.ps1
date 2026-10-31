[CmdletBinding()]
param([ValidateSet('windows','android')][string]$Platform='windows')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$build=Join-Path $repo 'build'
$resources=Join-Path $build 'cross-windows/engine/resources'
if($Platform -eq 'android'){$resources=Join-Path $build 'cross-android-arm64/engine/resources'}
if(!(Test-Path (Join-Path $resources 'vulkan/spk_math_probe.spv'))){throw 'Build the native engine first'}
$destination=Join-Path $build $(if($Platform -eq 'windows'){'app-windows'}else{'android-assets'})
New-Item -ItemType Directory -Force $destination,(Join-Path $destination 'resources'),(Join-Path $destination 'licenses') | Out-Null
Copy-Item -Path (Join-Path $resources '*') -Destination (Join-Path $destination 'resources') -Recurse -Force
$notices=@(
  @{path='build/deps/Vulkan-Headers/LICENSE.md';name='Vulkan-Headers-LICENSE.md'},
  @{path='license/LICENSE';name='GPL-3.0.txt'},
  @{path='license/SPEKTRAFILM_LICENSE.txt';name='CC-BY-SA-4.0.txt'},
  @{path='engine/resources_product/NOTICE.md';name='Product-profiles-NOTICE.md'},
  @{path='engine/resources_product/fonts/LICENSE';name='Liberation-fonts-LICENSE.txt'},
  @{path='engine/resources_product/fonts/DejaVu-LICENSE';name='DejaVu-font-LICENSE.txt'},
  @{path='engine/third_party/stb/LICENSE';name='stb-LICENSE.txt'},
  @{path='build/deps/LibRaw-0.22.2/LICENSE.LGPL';name='LibRaw-LGPL.txt'},
  @{path='build/deps/LibRaw-0.22.2/COPYRIGHT';name='LibRaw-COPYRIGHT.txt'}
)
$text="SpektraLab CrossPlatform`nSource: https://github.com/Godoshi9/SpektraLab-CrossPlatform`nBased on JamesQiu2005/SpektraLab (Windows_Linux a46627d250545a394ca21522f8a28b0fb89e4c42).`nFilm model and profiles: Andrea Volpato / spektrafilm. Additional profiles: Hanze Qiu.`nAll modifications in crossplatform/ and the portability fixes are GPL-3.0-or-later.`n"
foreach($notice in $notices){$source=Join-Path $repo $notice.path;Copy-Item -LiteralPath $source -Destination (Join-Path $destination ('licenses/'+$notice.name)) -Force;$text+="`n`n===== $($notice.name) =====`n"+[IO.File]::ReadAllText($source)}
$volk=Get-Content (Join-Path $repo 'engine/third_party/volk/volk.h') -Raw
$text+="`n`n===== volk (MIT) =====`n"+($volk.Substring($volk.LastIndexOf('Copyright (c)')))
if($Platform -eq 'windows'){
 $webview=Join-Path $env:USERPROFILE '.nuget/packages/microsoft.web.webview2/1.0.4258.31'
 foreach($name in @('LICENSE.txt','NOTICE.txt')){Copy-Item -LiteralPath (Join-Path $webview $name) -Destination (Join-Path $destination "licenses/WebView2-$name") -Force;$text+="`n`n===== WebView2 $name =====`n"+[IO.File]::ReadAllText((Join-Path $webview $name))}
 $dotnet=Join-Path $env:USERPROFILE '.nuget/packages/microsoft.netcore.app.runtime.win-x64/8.0.33'
 foreach($name in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')){if(Test-Path (Join-Path $dotnet $name)){Copy-Item -LiteralPath (Join-Path $dotnet $name) -Destination (Join-Path $destination "licenses/dotnet-$name") -Force;$text+="`n`n===== .NET $name =====`n"+[IO.File]::ReadAllText((Join-Path $dotnet $name))}}
 Copy-Item -LiteralPath (Join-Path $build 'cross-windows/Release/spektralab_mobile.dll') -Destination $destination -Force
 $vswhere='C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe'
 $vs=& $vswhere -latest -products '*' -property installationPath
 $redist=Get-ChildItem (Join-Path $vs 'VC/Redist/MSVC') -Directory | Where-Object Name -Match '^\d' | Sort-Object Name -Descending | Select-Object -First 1
 Get-ChildItem (Join-Path $redist.FullName 'x64/Microsoft.VC143.CRT') -Filter '*.dll' | Copy-Item -Destination $destination -Force
}else{
 $sdk=if($env:ANDROID_HOME){$env:ANDROID_HOME}else{Join-Path $env:LOCALAPPDATA 'Android/Sdk'}
 $ndkNotice=Join-Path $sdk 'ndk/30.0.16248370/NOTICE'
 Copy-Item -LiteralPath $ndkNotice -Destination (Join-Path $destination 'licenses/Android-NDK-NOTICE.txt') -Force
 $text+="`n`n===== Android NDK / libc++ =====`n"+[IO.File]::ReadAllText($ndkNotice)
 $sha=[Security.Cryptography.SHA256]::Create()
 $manifest=(Get-ChildItem (Join-Path $destination 'resources') -Recurse -File | Sort-Object FullName | ForEach-Object{ $_.FullName.Substring($destination.Length)+(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }) -join "`n"
 $stamp=[Convert]::ToHexString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($manifest))).ToLowerInvariant()
 [IO.File]::WriteAllText((Join-Path $destination 'resources-version.txt'),$stamp)
}
[IO.File]::WriteAllText((Join-Path $destination 'licenses/THIRD-PARTY-NOTICES.txt'),$text)
Write-Output "Staged $Platform resources and licenses: $destination"

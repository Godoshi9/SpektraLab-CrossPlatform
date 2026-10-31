[CmdletBinding()]
param([ValidateSet('windows','android','all')][string]$Platform='all',[string]$AndroidSdk=$env:ANDROID_HOME,[string]$NdkVersion='30.0.16248370',[switch]$RunTests,[switch]$Emulator)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Set-Location -LiteralPath $repo
if(!$AndroidSdk){$AndroidSdk=Join-Path $env:LOCALAPPDATA 'Android/Sdk'}
$ndk=Join-Path $AndroidSdk "ndk/$NdkVersion"
$vs=& 'C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe' -latest -products '*' -property installationPath
$cmake=Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
$ctest=Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/ctest.exe'
$ninja=Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/Ninja/ninja.exe'
$glslc=Join-Path $ndk 'shader-tools/windows-x86_64/glslc.exe'
foreach($tool in @($cmake,$glslc)){if(!(Test-Path $tool)){throw "Required build tool missing: $tool"}}
function Invoke-Checked([string]$exe,[string[]]$arguments){& $exe @arguments;if($LASTEXITCODE -ne 0){throw "$exe exited with $LASTEXITCODE"}}
& (Join-Path $repo 'engine/setup-libraw.ps1') -DependencyDirectory (Join-Path $repo 'build/deps')
$headers=Join-Path $repo 'build/deps/Vulkan-Headers'
if(!(Test-Path $headers)){Invoke-Checked git @('clone','--depth','1','--branch','vulkan-sdk-1.4.341.0','https://github.com/KhronosGroup/Vulkan-Headers.git',$headers)}
$commit=& git -C $headers rev-parse HEAD
if($commit -ne 'b5c8f996196ba4aa6d8f97e52b5d3b6e70f7e4e2'){throw 'Vulkan-Headers pin mismatch'}
if($Platform -in @('windows','all')){
 Invoke-Checked $cmake @('-S','crossplatform/native','-B','build/cross-windows','-G','Visual Studio 17 2022','-A','x64',"-DSPEKTRALAB_GLSLANG=$glslc")
 Invoke-Checked $cmake @('--build','build/cross-windows','--config','Release','--parallel','8')
 if($RunTests){Invoke-Checked $ctest @('--test-dir','build/cross-windows','-C','Release','--output-on-failure')}
 Invoke-Checked dotnet @('publish','crossplatform/windows/SpektraLab.csproj','-c','Release','-o','build/app-windows','--self-contained','true')
 & (Join-Path $PSScriptRoot 'stage.ps1') -Platform windows
 if($RunTests){$test=Start-Process -FilePath (Join-Path $repo 'build/app-windows/SpektraLab.exe') -ArgumentList @('--self-test',('"'+(Join-Path $repo 'build/windows-smoke')+'"')) -WindowStyle Hidden -PassThru -Wait;if($test.ExitCode -ne 0){throw 'Windows application test failed'}}
}
if($Platform -in @('android','all')){
 $abis=@(@{name='arm64-v8a';folder='arm64'})
 if($Emulator){$abis+=@{name='x86_64';folder='x64'}}
 foreach($abi in $abis){
  $dir="build/cross-android-$($abi.folder)"
  Invoke-Checked $cmake @('-S','crossplatform/native','-B',$dir,'-G','Ninja',"-DCMAKE_MAKE_PROGRAM=$ninja","-DCMAKE_TOOLCHAIN_FILE=$ndk/build/cmake/android.toolchain.cmake","-DANDROID_ABI=$($abi.name)",'-DANDROID_PLATFORM=android-28','-DCMAKE_BUILD_TYPE=Release','-DBUILD_TESTING=OFF',"-DSPEKTRALAB_GLSLANG=$glslc")
  Invoke-Checked $cmake @('--build',$dir,'--parallel','8')
  $jni=Join-Path $repo "build/android-jni/$($abi.name)";New-Item -ItemType Directory -Force $jni | Out-Null
  Copy-Item -LiteralPath "$dir/libspektralab_mobile.so" -Destination $jni -Force
  $triple=if($abi.name -eq 'arm64-v8a'){'aarch64-linux-android'}else{'x86_64-linux-android'}
  Copy-Item -LiteralPath "$ndk/toolchains/llvm/prebuilt/windows-x86_64/sysroot/usr/lib/$triple/libc++_shared.so" -Destination $jni -Force
 }
 & (Join-Path $PSScriptRoot 'stage.ps1') -Platform android
 [IO.File]::WriteAllText((Join-Path $repo 'crossplatform/android/local.properties'),'sdk.dir='+$AndroidSdk.Replace('\','/'))
 Invoke-Checked (Join-Path $repo 'crossplatform/android/gradlew.bat') @('-p','crossplatform/android','assembleDebug','assembleRelease')
}

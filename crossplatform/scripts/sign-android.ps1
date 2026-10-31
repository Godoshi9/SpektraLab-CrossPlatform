[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$private=Join-Path $repo 'crossplatform/.signing'
New-Item -ItemType Directory -Force $private | Out-Null
$configPath=Join-Path $private 'signing.json'
if(!(Test-Path $configPath)){
 $password=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(36))
 $config=@{password=$password;alias='spektralab';keystore=(Join-Path $private 'spektralab-release.jks')}
 $env:SPEKTRA_GENERATED_PASSWORD=$password
 & keytool -genkeypair -keystore $config.keystore -storetype JKS -alias $config.alias -keyalg RSA -keysize 3072 -validity 10000 -dname 'CN=Godoshi9, OU=SpektraLab CrossPlatform, O=SpektraLab' -storepass:env SPEKTRA_GENERATED_PASSWORD -keypass:env SPEKTRA_GENERATED_PASSWORD
 if($LASTEXITCODE -ne 0){throw 'Signing key generation failed'}
 [IO.File]::WriteAllText($configPath,($config|ConvertTo-Json))
 Remove-Item Env:SPEKTRA_GENERATED_PASSWORD
}
$config=Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
try{
 $env:SPEKTRA_KEYSTORE=$config.keystore;$env:SPEKTRA_STORE_PASSWORD=$config.password;$env:SPEKTRA_KEY_PASSWORD=$config.password;$env:SPEKTRA_KEY_ALIAS=$config.alias
 & (Join-Path $repo 'crossplatform/android/gradlew.bat') -p (Join-Path $repo 'crossplatform/android') assembleRelease
 if($LASTEXITCODE -ne 0){throw 'Signed Android release build failed'}
 New-Item -ItemType Directory -Force (Join-Path $repo 'dist') | Out-Null
 Copy-Item -LiteralPath (Join-Path $repo 'crossplatform/android/app/build/outputs/apk/release/app-release.apk') -Destination (Join-Path $repo 'dist/SpektraLab-0.1.0-Android.apk') -Force
}finally{Remove-Item Env:SPEKTRA_KEYSTORE,Env:SPEKTRA_STORE_PASSWORD,Env:SPEKTRA_KEY_PASSWORD,Env:SPEKTRA_KEY_ALIAS -ErrorAction SilentlyContinue}
Write-Output 'Signed APK created in dist. Back up crossplatform/.signing privately for future updates.'

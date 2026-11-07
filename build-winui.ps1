param([switch]$DisableNuGetAudit)
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
$project='src\TokenWatch.WinUI\TokenWatch.WinUI.csproj'
$dotnet='C:\Program Files\dotnet\dotnet.exe'
if(-not(Test-Path -LiteralPath $dotnet)){ $dotnet='dotnet' }
$version=([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ }
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Expected a three-part project version'}
$release=Join-Path $PSScriptRoot "dist\TokenWatch-v$version"
New-Item -ItemType Directory -Path $release -Force | Out-Null
[string[]]$auditProperties=if($DisableNuGetAudit){@('-p:NuGetAudit=false')}else{@()}
foreach($edition in @('self-contained','framework-dependent')) {
    $staging=Join-Path $PSScriptRoot "artifacts\winui-publish\v$version\$edition"
    [string[]]$publishProperties=if($edition -eq 'self-contained') {
        @('--self-contained','true','-p:EnableMsixTooling=true','-p:PublishSingleFile=true','-p:IncludeAllContentForSelfExtract=true','-p:IncludeNativeLibrariesForSelfExtract=true','-p:EnableCompressionInSingleFile=true')
    } else {
        @('--self-contained','false','-p:TokenWatchEdition=FrameworkDependent','-p:PublishSingleFile=false')
    }
    & $dotnet publish $project -c Release -r win-x64 @publishProperties @auditProperties -o $staging --nologo
    if($LASTEXITCODE -ne 0){throw "WinUI $edition publish failed"}
    $name=if($edition -eq 'self-contained'){'TokenWatch.exe'}else{'TokenWatch-framework-dependent.exe'}
    if($edition -eq 'framework-dependent') {
        Get-ChildItem -LiteralPath $staging | Where-Object Name -NotLike '*.pdb' | Copy-Item -Destination $release -Recurse -Force
    } else { Copy-Item -LiteralPath (Join-Path $staging $name) -Destination (Join-Path $release $name) -Force }
    $info=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $release $name))
    if($info.FileVersion -ne "$version.0" -or $info.ProductVersion.Split('+')[0] -ne $version){throw "Version mismatch: $name"}
    Copy-Item -LiteralPath 'src\TokenWatch.WinUI\README.md' -Destination (Join-Path $release 'README.md') -Force
    Copy-Item -LiteralPath 'src\TokenWatch.WinUI\THIRD-PARTY-NOTICES.md' -Destination $release -Force
    $licenses=Join-Path $release 'licenses'
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    Copy-Item -LiteralPath 'src\TokenWatch.WinUI\Assets\OCTICONS-LICENSE.txt' -Destination $licenses -Force
    $packageRoot=if($env:NUGET_PACKAGES){$env:NUGET_PACKAGES}else{Join-Path $env:USERPROFILE '.nuget\packages'}
    foreach($package in @(@('microsoft.windowsappsdk','WINDOWS-APP-SDK'),@('microsoft.windowsappsdk.base','WINDOWS-APP-SDK-BASE'),@('microsoft.windowsappsdk.winui','WINUI'))) {
        Copy-Item -LiteralPath (Join-Path $packageRoot ($package[0]+'\2.6.5\license.txt')) -Destination (Join-Path $licenses ($package[1]+'-LICENSE.txt')) -Force
        Copy-Item -LiteralPath (Join-Path $packageRoot ($package[0]+'\2.6.5\NOTICE.txt')) -Destination (Join-Path $licenses ($package[1]+'-NOTICE.txt')) -Force
    }
    $runtimeRoot=Join-Path $packageRoot 'microsoft.netcore.app.runtime.win-x64'
    $runtime=Get-ChildItem -LiteralPath $runtimeRoot -Directory | Where-Object Name -Like '10.0.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    Copy-Item -LiteralPath (Join-Path $runtime.FullName 'LICENSE.TXT') -Destination (Join-Path $licenses 'DOTNET10-LICENSE.txt') -Force
    Copy-Item -LiteralPath (Join-Path $runtime.FullName 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $licenses 'DOTNET10-THIRD-PARTY-NOTICES.txt') -Force
    if($edition -eq 'self-contained') {
        $zipPaths=@((Join-Path $release $name),(Join-Path $release 'README.md'),(Join-Path $release 'THIRD-PARTY-NOTICES.md'),$licenses)
        $zipName="TokenWatch-v$version-win-x64.zip"
    } else {
        $zipPaths=@(Get-ChildItem -LiteralPath $staging | Where-Object Name -NotLike '*.pdb' | Select-Object -ExpandProperty FullName) + @((Join-Path $release 'README.md'),(Join-Path $release 'THIRD-PARTY-NOTICES.md'),$licenses)
        $zipName="TokenWatch-v$version-win-x64-framework-dependent.zip"
    }
    Compress-Archive -Path $zipPaths -DestinationPath (Join-Path $PSScriptRoot "dist\$zipName") -Force
    Get-Item -LiteralPath (Join-Path $release $name),(Join-Path $PSScriptRoot "dist\$zipName") | Select-Object FullName,Length
}

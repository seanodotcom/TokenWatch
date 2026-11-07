param([switch]$Test,[switch]$Publish,[switch]$FrameworkDependent,[switch]$DisableNuGetAudit,[switch]$CoreTestOnly,[string]$CertificateThumbprint)
$ErrorActionPreference='Stop'
if($FrameworkDependent -and -not $Publish){throw '-FrameworkDependent requires -Publish'}
if($CoreTestOnly -and $Publish){throw 'A release requires the native desktop tests'}
Set-Location -LiteralPath $PSScriptRoot
$dotnet=(Get-Command dotnet -ErrorAction Stop).Source
$env:DOTNET_CLI_HOME=Join-Path $PSScriptRoot '.tools\dotnet-home'
if(-not $env:NUGET_PACKAGES){$env:NUGET_PACKAGES=Join-Path $env:USERPROFILE '.nuget\packages'}
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_NOLOGO='1'
if((& $dotnet --version) -notmatch '^10\.'){throw 'Install the .NET 10 SDK. global.json selects the current .NET 10 feature band.'}
$project='src\TokenWatch\TokenWatch.csproj'
$version=([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Expected a three-part release version'}
[string[]]$properties=if($DisableNuGetAudit){@('-p:NuGetAudit=false')}else{@()}
& $dotnet build $project -c Release --nologo @properties
if($LASTEXITCODE -ne 0){throw 'Build failed'}
$runRoot=Join-Path $PSScriptRoot ('artifacts\verification\v'+$version+'-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
function Invoke-NativeSmoke([string]$Executable,[string]$Label) {
    $report=Join-Path $runRoot ($Label+'.json')
    $settings=Join-Path $runRoot ($Label+'.settings.json')
    $arguments=@('--demo','--isolated','--settings',('"'+$settings+'"'),'--ui-smoke',('"'+$report+'"'))
    $process=Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $deadline=[DateTime]::UtcNow.AddMinutes(3)
    while(-not $process.WaitForExit(1000)) {
        if([DateTime]::UtcNow -ge $deadline){$process.Kill(); throw "$Label native test timed out"}
    }
    if(-not(Test-Path -LiteralPath $report)){throw "$Label produced no native test report"}
    $results=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    Write-Output ("{0}: {1} passed, {2} failed; {3}" -f $Label,$results.Passed,$results.Failed,$report)
    if($results.Failed -ne 0 -or $results.Passed -eq 0 -or $process.ExitCode -ne 0){
        $results.Checks | Where-Object {-not $_.Passed} | Format-List | Out-Host
        throw "$Label native tests failed"
    }
}
if($Test -or $Publish -or $CoreTestOnly) {
    & $dotnet run --project 'tests\TokenWatch.Tests\TokenWatch.Tests.csproj' -c Release @properties
    if($LASTEXITCODE -ne 0){throw 'Core tests failed'}
    if(-not $CoreTestOnly){Invoke-NativeSmoke (Join-Path $PSScriptRoot 'src\TokenWatch\bin\Release\net10.0-windows\TokenWatch.exe') 'source'}
    if(-not $DisableNuGetAudit) {
        $auditLines=& $dotnet list $project package --vulnerable --include-transitive --no-restore --format json
        if($LASTEXITCODE -ne 0){throw 'Dependency vulnerability audit failed'}
        $auditText=$auditLines -join [Environment]::NewLine
        $auditText | Set-Content -LiteralPath (Join-Path $runRoot 'dependency-audit.json')
        $audit=$auditText | ConvertFrom-Json
        $vulnerabilities=@($audit.projects | ForEach-Object {$_.frameworks} | ForEach-Object {@($_.topLevelPackages)+@($_.transitivePackages)} | ForEach-Object {$_.vulnerabilities} | Where-Object {$_})
        if($vulnerabilities.Count -gt 0 -or @($audit.logs | Where-Object {$_.level -eq 'error'}).Count -gt 0){throw 'Dependency audit reported vulnerabilities or errors'}
        Write-Output 'Dependency audit passed: no known vulnerable packages.'
    }
}
if(-not $Publish){return}
if(-not(Test-Path -LiteralPath 'LICENSE' -PathType Leaf)){throw 'A release requires the project LICENSE file'}
$release=Join-Path $PSScriptRoot "dist\TokenWatch-v$version"
New-Item -ItemType Directory -Path $release -Force | Out-Null
$runtimeVersion=@(& $dotnet --list-runtimes | Where-Object {$_ -match '^Microsoft.WindowsDesktop.App (10\.\d+\.\d+) '} | ForEach-Object {[version]($_.Split(' ')[1])} | Sort-Object -Descending)[0].ToString()
$checksums=@()
foreach($edition in @('self-contained','framework-dependent')) {
    $stage=Join-Path $runRoot $edition
    $publishDirectory=Join-Path $stage 'publish'
    $selfContained=if($edition -eq 'self-contained'){'true'}else{'false'}
    & $dotnet publish $project -c Release -r win-x64 --self-contained $selfContained -p:RuntimeFrameworkVersion=$runtimeVersion -o $publishDirectory --nologo @properties
    if($LASTEXITCODE -ne 0){throw "Publish failed: $edition"}
    $package=Join-Path $stage 'package'
    New-Item -ItemType Directory -Path $package -Force | Out-Null
    $name=if($edition -eq 'self-contained'){'TokenWatch.exe'}else{'TokenWatch-framework-dependent.exe'}
    $executable=Join-Path $package $name
    Copy-Item -LiteralPath (Join-Path $publishDirectory 'TokenWatch.exe') -Destination $executable
    $info=[Diagnostics.FileVersionInfo]::GetVersionInfo($executable)
    if($info.FileVersion -ne "$version.0" -or $info.ProductVersion.Split('+')[0] -ne $version){throw "Executable version mismatch: $name"}
    if($CertificateThumbprint) {
        $certificate=Get-Item -LiteralPath ("Cert:\CurrentUser\My\"+$CertificateThumbprint)
        $signature=Set-AuthenticodeSignature -FilePath $executable -Certificate $certificate -TimestampServer 'http://timestamp.digicert.com' -HashAlgorithm SHA256
        if($signature.Status -ne 'Valid'){throw "Signing failed: $name"}
    }
    Copy-Item -LiteralPath 'README.md','THIRD-PARTY-NOTICES.md' -Destination $package
    Copy-Item -LiteralPath 'LICENSE' -Destination $package
    if(Test-Path -LiteralPath 'PRIVACY.md'){Copy-Item -LiteralPath 'PRIVACY.md' -Destination $package}
    $licenses=Join-Path $package 'licenses'
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    Copy-Item -LiteralPath 'licenses\OCTICONS-LICENSE.txt' -Destination $licenses
    foreach($runtime in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64')) {
        $root=Join-Path $env:NUGET_PACKAGES ($runtime+'\'+$runtimeVersion)
        $prefix=if($runtime -like '*windowsdesktop*'){'WINDOWS-DESKTOP'}else{'DOTNET'}
        $noticeNames=if($prefix -eq 'WINDOWS-DESKTOP'){@('LICENSE')}else{@('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')}
        foreach($notice in $noticeNames) {
            Copy-Item -LiteralPath (Join-Path $root $notice) -Destination (Join-Path $licenses ($prefix+'-'+$notice))
        }
    }
    $managementRoot=Join-Path $env:NUGET_PACKAGES 'system.management\8.0.0'
    Copy-Item -LiteralPath (Join-Path $managementRoot 'LICENSE.TXT'),(Join-Path $managementRoot 'THIRD-PARTY-NOTICES.TXT') -Destination $licenses
    $zipName=if($edition -eq 'self-contained'){"TokenWatch-v$version-win-x64.zip"}else{"TokenWatch-v$version-win-x64-framework-dependent.zip"}
    $candidateZip=Join-Path $stage $zipName
    Compress-Archive -Path (Join-Path $package '*') -DestinationPath $candidateZip
    $extracted=Join-Path $stage 'extracted'
    Expand-Archive -LiteralPath $candidateZip -DestinationPath $extracted
    if((Get-FileHash -LiteralPath (Join-Path $extracted 'LICENSE') -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath 'LICENSE' -Algorithm SHA256).Hash){throw 'The packaged project license does not match LICENSE'}
    $unexpected=@(Get-ChildItem -LiteralPath $extracted -Recurse -File | Where-Object {$_.Name -match '(?i)settings\.json|credentials|\.pdb$|\.log$|error\.txt'})
    if($unexpected.Count -gt 0){throw 'A package contains private or development files'}
    Invoke-NativeSmoke (Join-Path $extracted $name) $edition
    Copy-Item -LiteralPath $executable -Destination (Join-Path $release $name) -Force
    foreach($file in @('README.md','THIRD-PARTY-NOTICES.md','PRIVACY.md','LICENSE','licenses')) {
        $source=Join-Path $package $file
        if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination $release -Recurse -Force}
    }
    $zip=Join-Path $PSScriptRoot ('dist\'+$zipName)
    Copy-Item -LiteralPath $candidateZip -Destination $zip -Force
    $checksums+=((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$zipName)
    $checksums+=((Get-FileHash -LiteralPath (Join-Path $release $name) -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+"TokenWatch-v$version/$name")
    Get-Item -LiteralPath (Join-Path $release $name),$zip | Select-Object FullName,Length
}
$checksums | Set-Content -LiteralPath (Join-Path $PSScriptRoot "dist\TokenWatch-v$version-SHA256SUMS.txt") -Encoding ascii
Write-Output "Release $version verified. Reports: $runRoot"

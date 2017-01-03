$artifact_zip = 'https://ci.dot.net/job/dotnet_orleans/job/master/job/netfx/lastSuccessfulBuild/artifact/Binaries/NuGet.Packages/Prerelease/*zip*/Prerelease.zip'
$work_root = 'Orleans'
$work_path = "$work_root\temp"
$source_path = "$work_root\prerelease-packages"

Write-Host "Artifact URL: $artifact_zip"
Write-Host "Working Directory: $PSScriptRoot"
Write-Host "Root: $work_root"
Write-Host "Working Directory: $work_path"
Write-Host "Sources Directory: $source_path"

Set-Location -Path $PSScriptRoot

If (-not (Test-Path -Path $work_root -PathType Container)) {
  New-Item -Path $work_root -ItemType Directory
}

If (Test-Path -Path $work_path -PathType Container) {
  Remove-Item -Path $work_path -Recurse
}
New-Item -Path $work_path -ItemType Directory

If (-not (Test-Path -Path $source_path -PathType Container)) {
  New-Item -Path $source_path -ItemType Directory
}

Invoke-WebRequest -Uri $artifact_zip -Method Get -OutFile "$work_path\Prerelease.zip"

Expand-Archive -Path "$work_path\Prerelease.zip" -DestinationPath $work_path

# Remove source packages
Remove-Item -Path "$work_path\Prerelease\*.symbols.nupkg"

& .\nuget.exe init "$work_path\Prerelease" "$source_path"

Write-Host "Done"

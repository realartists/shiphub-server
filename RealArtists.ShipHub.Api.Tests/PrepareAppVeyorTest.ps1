$old_string = "Server=(local);Database=ShipHubTest;User=ShipUser;Password=uB4vtZbsjUGvqzmS0S6i;MultipleActiveResultSets=true"
$new_string = "Server=(local)\SQL2016;Database=shiphub-dev-test;User=ShipUser;Password=uB4vtZbsjUGvqzmS0S6i;MultipleActiveResultSets=True;Connection Timeout=30;"

Write-Host "Original: $old_string"
Write-Host "Replacement: $new_string"
Write-Host "Working Directory: $PSScriptRoot"

Get-ChildItem *.config -Recurse -Path "$PSScriptRoot\bin" | ForEach {
  $fullPath = $_.FullName
  Write-Host $fullPath
  (Get-Content $fullPath | ForEach {$_ -replace [regex]::escape($old_string), $new_string}) | Set-Content $fullPath
}

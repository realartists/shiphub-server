$old_string = "Server=(local);Database=ShipHubTest;User=ShipUser;Password=uB4vtZbsjUGvqzmS0S6i;MultipleActiveResultSets=true"
$new_string = "Server=tcp:shiphub-dev-dbserver.database.windows.net,1433;Persist Security Info=False;Database=shiphub-dev-test;User=ShipUser;Password=uB4vtZbsjUGvqzmS0S6i;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"

Get-ChildItem *.config -Recurse | ForEach {
(Get-Content $_ | ForEach {$_ -replace $old_string, $new_string}) | Set-Content $_
}

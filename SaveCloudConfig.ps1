param (
  [string]
  $ServiceName,
  
  [string]
  [ValidateSet('Production','Staging')]
  $ServiceSlot,
  
  [string] $Output
)

$deployment = Get-AzureDeployment -ServiceName $ServiceName

$configuration = [xml]$deployment.Configuration

$configuration.Save($Output)

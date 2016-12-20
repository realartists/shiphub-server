# shiphub-server ![Build Status](https://realartists.visualstudio.com/_apis/public/build/definitions/88275168-cb10-4b52-bf29-9eb07b033ef7/3/badge)
Aspires to be as good as GHSyncConnection.

##Deployment
I tried to keep it simple but despite my best efforts it's complicated now.

### DB on Azure SQL
TODO

### App Server on Azure Websites
TODO

### Web Job on Azure Websites
TODO

### Orleans on Cloud Services
#### Prerequisites
* Visual Studio 2015 Pro/Enterprise Update 3+ (Get from MSDN)
* [Azure SDK 2.9.6+](https://azure.microsoft.com/en-us/downloads/)

#### Configuration
For security reasons we don't commit configuration secrets to git. If required, the Dev and Live configurations in git can be updated with fresh values, but that's a pain. Instead, download the running configuration here:

* [Dev](https://portal.azure.com/#resource/subscriptions/b9f28aae-2074-4097-b5ce-ec28f68c4981/resourceGroups/ShipHub-Dev/providers/Microsoft.ClassicCompute/domainNames/shiphub-dev-cs/configuration)
* [Live](https://portal.azure.com/#resource/subscriptions/b9f28aae-2074-4097-b5ce-ec28f68c4981/resourceGroups/ShipHub-Live/providers/Microsoft.ClassicCompute/domainNames/shiphub-live-cs/configuration)

Just save over the example files. **DON'T FORGET AND COMMIT THEM LATER!**

#### Deployment
To actually deploy the cluster, right click on the `RealArtists.ShipHub.CloudServices` project and select `Publish`. Select the right target profile and go. It'll between 3 and 35 minutes. Yes, really.


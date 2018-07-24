#  Ship: A GitHub Issues and Pull Requests App for macOS

Ship was formerly a commercial GitHub Issues and Pull Requests client distributed by [Real Artists, Inc](https://www.realartists.com). The product is now discontinued, and the source code is now publicly available here.

While Real Artists has no intention of developing the product further, therefore will not be reviewing or accepting pull requests, anyone so inclined is welcome to fork the repository or copy any parts of the code.

# shiphub-server: The Magic Behind the Scenes

So you want to run [Ship](https://github.com/realartists/shiphub-cocoa) and need a server to point it at? Or maybe you just want to see how it all worked? You're in the right place. This README will cover the basics you need to know to run Ship's server component locally for development.

This guide is neither detailed nor complete. It should be just enough for someone familiar with C#, Visual Studio, and Azure to get things going.

## Prerequisites

While I tried to keep the server simple, I failed. The Azure SDK emulators are almost enough, but they lack the required Service Bus features. You'll need an Azure account.

#### SQL Server 2017

Grab the latest [SQL Server](https://www.microsoft.com/en-us/sql-server/sql-server-downloads). I only tested with Developer Edition, but Express *might* work. You probably also want [SQL Server Management Studio](https://docs.microsoft.com/en-us/sql/ssms/download-sql-server-management-studio-ssms?view=sql-server-2017) or [SQL Operations Studio](https://docs.microsoft.com/en-us/sql/sql-operations-studio/download?view=sql-server-2017), but neither is required.

#### Visual Studio 2017

Grab the latest [Visual Studio](https://visualstudio.microsoft.com/downloads/). Community is fine. You'll need the "ASP.NET and Web Development," "Azure Development," and "Data Storage and Processing" roles.

#### Azure Service Bus

Create an Azure Service Bus namespace [here](https://portal.azure.com/#create/Microsoft.ServiceBus). Note the credentials, since you'll need them later.

## Configuration

Clone the repository and open `RealArtists.ShipHub.sln`. If Visual Studio complains or won't load some of the projects, install any missing tooling.

Build the solution. This will do two important things:

  1. Restore all needed NuGet packages.
  2. Create configuration files in which to store your secrets.

Open the "Solution Items" and set appropriate values in the `Secret.AppSettings.config` and `Secret.ConnectionStrings.config` files. 

Open the `RealArtists.ShipHub.CloudServices` project and set proper values in the `ServiceConfiguration.Local.cscfg` file.

Rebuild the solution to propogate your changes.

## Database

[Enable SQL Authentication](https://docs.microsoft.com/en-us/sql/database-engine/configure-windows/change-server-authentication-mode?view=sql-server-2017). You need not (and should not) enable the `sa` account.

Enable contained databases:

    sp_configure 'contained database authentication', 1;
    GO
    RECONFIGURE;
    GO

The `RealArtists.ShipHub.Database` project contains the schema you'll need to run Ship. Deploy the generated dacpac file to the database you specified in `Secret.ConnectionStrings.config`.

Make the database you're using for ship contained:

    USE [master]
    GO
    ALTER DATABASE [ShipHub] SET CONTAINMENT = PARTIAL
    GO

## IIS or IIS Express

You'll need a certificate your Mac (running the Ship client) thinks is valid. You can get a real one or generate your own. Install the certificate.

Register `RealArtists.ShipHub.Api` with IIS or IIS Express with a TLS 1.2 Endpoint.

## Start All the Things

To run Ship, you need to at a minimum be running the Orleans silo and the API. Open the solution properties, select `Startup Project`, `Multiple Startup Projects` and ensure `RealArtists.ShipHub.Api` and `RealArtists.ShipHub.CloudServices` are set to start.

If you get an error saying, "Unable to get setting value Parameter name: profileName," close Visual Studio. It'll work when you re-open it ¯\\_(ツ)_/¯

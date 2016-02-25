CREATE TABLE [Ship].[Users] (
  [Id]              UNIQUEIDENTIFIER ROWGUIDCOL NOT NULL,
  [GitHubAccountId] INT              NOT NULL,
  [CreationDate]    DATETIMEOFFSET   NOT NULL,
  CONSTRAINT [PK_ShipUsers] PRIMARY KEY CLUSTERED ([Id] ASC),
  CONSTRAINT [FK_ShipUsers_GitHubAccountId_GitHubAccounts_Id] FOREIGN KEY ([GitHubAccountId]) REFERENCES [GitHub].[Accounts] ([Id])
);
GO

CREATE NONCLUSTERED INDEX [IX_ShipUsers_GitHubAccountId] ON [Ship].[Users]([GitHubAccountId] ASC);
GO

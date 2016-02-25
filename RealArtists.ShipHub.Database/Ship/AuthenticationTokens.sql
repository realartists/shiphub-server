CREATE TABLE [Ship].[AuthenticationTokens] (
  [Id]             UNIQUEIDENTIFIER ROWGUIDCOL NOT NULL,
  [UserId]         UNIQUEIDENTIFIER NOT NULL,
  [ClientName]     NVARCHAR (150)   NOT NULL,
  [CreationDate]   DATETIMEOFFSET   NOT NULL,
  [LastAccessDate] DATETIMEOFFSET   NOT NULL,
  CONSTRAINT [PK_ShipAuthenticationTokens] PRIMARY KEY CLUSTERED ([Id] ASC),
  CONSTRAINT [FKCD_ShipAuthenticationTokens_UserId_ShipUsers_Id] FOREIGN KEY ([UserId]) REFERENCES [Ship].[Users] ([Id]) ON DELETE CASCADE
);
GO

CREATE NONCLUSTERED INDEX [IX_ShipAuthenticationTokens_UserId] ON [Ship].[AuthenticationTokens]([UserId] ASC);
GO

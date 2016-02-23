CREATE TABLE [dbo].[AuthenticationTokens] (
  [AccessToken] NVARCHAR(512) NOT NULL,
  [AccountId]   INT           NOT NULL,
  [Scopes]      NVARCHAR(MAX) NOT NULL,
  CONSTRAINT [PK_GitHub_AuthenticationTokens] PRIMARY KEY CLUSTERED ([AccessToken] ASC),
  CONSTRAINT [FKCD_GitHub_AuthenticationTokens_Accounts] FOREIGN KEY ([AccountId]) REFERENCES [GitHub].[Accounts] ([Id]) ON DELETE CASCADE
);
GO

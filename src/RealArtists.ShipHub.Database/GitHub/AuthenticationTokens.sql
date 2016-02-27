CREATE TABLE [GitHub].[AuthenticationTokens] (
  [AccountId]   INT           NOT NULL,
  [AccessToken] NVARCHAR(512) NOT NULL,
  [Scopes]      NVARCHAR(MAX) NOT NULL,
  CONSTRAINT [PK_GitHub_AuthenticationTokens] PRIMARY KEY CLUSTERED ([AccountId] ASC),
  CONSTRAINT [FKCD_GitHub_AuthenticationTokens_Accounts] FOREIGN KEY ([AccountId]) REFERENCES [GitHub].[Accounts] ([Id]) ON DELETE CASCADE
);
GO

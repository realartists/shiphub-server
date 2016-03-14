CREATE TABLE [dbo].[AccessTokens] (
  [AccountId]          INT            NOT NULL,
  [ApplicationId]      NVARCHAR(64)   NOT NULL,
  [Token]              NVARCHAR(64)   NOT NULL,
  [Scopes]             NVARCHAR(255)  NOT NULL,
  [RateLimit]          INT            NOT NULL,
  [RateLimitRemaining] INT            NOT NULL,
  [RateLimitReset]     DATETIMEOFFSET NOT NULL,
  CONSTRAINT [PK_AccessTokens] PRIMARY KEY CLUSTERED ([AccountId] ASC),
  CONSTRAINT [FKCD_AccessTokens_AccountId_Accounts_Id] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts] ([Id]) ON DELETE CASCADE
);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_AccessTokens_Token] ON [dbo].[AccessTokens]([Token]);
GO

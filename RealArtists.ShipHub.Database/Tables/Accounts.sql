CREATE TABLE [dbo].[Accounts] (
  [Id]                 BIGINT         NOT NULL,
  [Type]               NVARCHAR(4)    NOT NULL,
  [Login]              NVARCHAR(255)  NOT NULL,
  [Date]               DATETIMEOFFSET NOT NULL,
  [MetaDataJson]       NVARCHAR(MAX)  NULL,
  [RepoMetaDataJson]   NVARCHAR(MAX)  NULL,
  [OrgMetaDataJson]    NVARCHAR(MAX)  NULL,
  [Token]              NVARCHAR(64)   NULL,
  [Scopes]             NVARCHAR(255)  NOT NULL,
  [RateLimit]          INT            NOT NULL,
  [RateLimitRemaining] INT            NOT NULL,
  [RateLimitReset]     DATETIMEOFFSET NOT NULL,
  CONSTRAINT [PK_Accounts] PRIMARY KEY CLUSTERED ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_Accounts_Type] ON [dbo].[Accounts]([Type])
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Accounts_Login] ON [dbo].[Accounts]([Login])
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Accounts_Token]
  ON [dbo].[Accounts]([Token])
  WHERE ([Token] IS NOT NULL)
GO

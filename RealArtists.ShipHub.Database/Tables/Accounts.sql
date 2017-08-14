CREATE TABLE [dbo].[Accounts] (
  [Id]                  BIGINT         NOT NULL,
  [Type]                NVARCHAR(4)    NOT NULL,
  [Login]               NVARCHAR(255)  NOT NULL,
  [Name]                NVARCHAR(255)  NULL,
  [Email]               NVARCHAR(320)  NULL,
  [Date]                DATETIMEOFFSET NOT NULL,
  [MetadataJson]        NVARCHAR(MAX)  NULL,
  [RepoMetadataJson]    NVARCHAR(MAX)  NULL,
  [OrgMetadataJson]     NVARCHAR(MAX)  NULL,
  [ProjectMetadataJson] NVARCHAR(MAX)  NULL,
  [Scopes]              NVARCHAR(255)  NOT NULL DEFAULT '',
  [RateLimit]           INT            NOT NULL DEFAULT 0,
  [RateLimitRemaining]  INT            NOT NULL DEFAULT 0,
  [RateLimitReset]      DATETIMEOFFSET NOT NULL DEFAULT '1970-01-01T00:00:00Z',
  [MentionSince]        DATETIMEOFFSET NULL,
  [MentionMetadataJson] NVARCHAR(MAX)  NULL,
  CONSTRAINT [PK_Accounts] PRIMARY KEY CLUSTERED ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_Accounts_Type] ON [dbo].[Accounts]([Type])
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Accounts_Login] ON [dbo].[Accounts]([Login])
GO

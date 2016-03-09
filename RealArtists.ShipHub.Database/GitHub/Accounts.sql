CREATE TABLE [GitHub].[Accounts] (
  [Id]           INT            NOT NULL,
  [AvatarUrl]    NVARCHAR(500)  NULL,
  [Company]      NVARCHAR(255)  NULL,
  [Login]        NVARCHAR(255)  NOT NULL,
  [Name]         NVARCHAR(255)  NULL,
  [CreatedAt]    DATETIMEOFFSET NOT NULL,
  [UpdatedAt]    DATETIMEOFFSET NOT NULL,
  [ETag]         NVARCHAR(64)   NULL,
  [Expires]      DATETIMEOFFSET NULL,
  [LastModified] DATETIMEOFFSET NULL,
  [LastRefresh]  DATETIMEOFFSET NOT NULL,
  CONSTRAINT [PK_GitHub_Accounts] PRIMARY KEY CLUSTERED ([Id] ASC),
);
GO

CREATE TYPE [dbo].[ProjectTableType] AS TABLE
(
  [Id]              BIGINT            NOT NULL PRIMARY KEY CLUSTERED,
  [Name]            NVARCHAR(255)     NOT NULL,
  [Number]          BIGINT            NOT NULL,
  [Body]            NVARCHAR(MAX)     NULL,
  [CreatedAt]       DATETIMEOFFSET    NOT NULL,
  [UpdatedAt]       DATETIMEOFFSET    NOT NULL,
  [CreatorId]       BIGINT            NOT NULL
)

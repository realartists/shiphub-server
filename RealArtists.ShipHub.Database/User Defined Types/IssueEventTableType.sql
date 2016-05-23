CREATE TYPE [dbo].[IssueEventTableType] AS TABLE (
  [Id]            BIGINT         NOT NULL PRIMARY KEY CLUSTERED,
  [CreatedAt]     DATETIMEOFFSET NOT NULL,
  [ExtensionData] NVARCHAR(MAX)  NOT NULL
)

CREATE TYPE [dbo].[IssueEventTableType] AS TABLE (
  [Id]            INT            NOT NULL PRIMARY KEY CLUSTERED,
  [CreatedAt]     DATETIMEOFFSET NOT NULL,
  [ExtensionData] NVARCHAR(MAX)  NOT NULL
)

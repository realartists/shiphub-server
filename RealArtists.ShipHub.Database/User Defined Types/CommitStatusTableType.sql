CREATE TYPE [dbo].[CommitStatusTableType] AS TABLE(
  [Id]           BIGINT         NOT NULL PRIMARY KEY CLUSTERED,
  [State]        NVARCHAR(MAX)  NULL,
  [TargetUrl]    NVARCHAR(MAX)  NULL,
  [Description]  NVARCHAR(MAX)  NULL,
  [Context]      NVARCHAR(MAX)  NULL,
  [CreatedAt]    DATETIMEOFFSET NOT NULL,
  [UpdatedAt]    DATETIMEOFFSET NOT NULL
)

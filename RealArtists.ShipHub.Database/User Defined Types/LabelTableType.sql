CREATE TYPE [dbo].[LabelTableType] AS TABLE (
  [Id]     BIGINT        NOT NULL PRIMARY KEY CLUSTERED,
  [Color]  CHAR(6)       NOT NULL,
  [Name]   NVARCHAR(400) NOT NULL
)

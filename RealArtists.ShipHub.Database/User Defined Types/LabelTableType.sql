CREATE TYPE [dbo].[LabelTableType] AS TABLE (
  [Id]     BIGINT        NOT NULL,
  [Color]  CHAR(6)       NOT NULL,
  [Name]   NVARCHAR(400) NOT NULL,
  [IssueId] BIGINT
)

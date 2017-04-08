CREATE TYPE [dbo].[IssueMappingTableType] AS TABLE (
  [IssueNumber] INT    NOT NULL,
  [IssueId]     BIGINT NULL,
  [MappedId]    BIGINT NOT NULL,
  PRIMARY KEY CLUSTERED ([IssueNumber], [MappedId])
)

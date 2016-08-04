CREATE TYPE [dbo].[CommentTableType] AS TABLE (
  [Id]           BIGINT         NOT NULL PRIMARY KEY CLUSTERED,
  [IssueNumber]  INT            NOT NULL,
  [UserId]       BIGINT         NOT NULL,
  [Body]         NVARCHAR(MAX)  NOT NULL,
  [CreatedAt]    DATETIMEOFFSET NOT NULL,
  [UpdatedAt]    DATETIMEOFFSET NOT NULL
)

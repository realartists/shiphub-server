CREATE TYPE [dbo].[CommentTableType] AS TABLE (
  [Id]           INT            NOT NULL,
  [IssueNumber]  INT            NOT NULL,
  [UserId]       INT            NOT NULL,
  [Body]         NVARCHAR(MAX)  NOT NULL,
  [CreatedAt]    DATETIMEOFFSET NOT NULL,
  [UpdatedAt]    DATETIMEOFFSET NOT NULL,
  [Reactions]    NVARCHAR(300)  NULL
)

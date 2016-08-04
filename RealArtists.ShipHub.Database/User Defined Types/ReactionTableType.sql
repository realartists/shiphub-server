CREATE TYPE [dbo].[ReactionTableType] AS TABLE (
  [Id]           BIGINT         NOT NULL,
  [UserId]       BIGINT         NOT NULL,
  [IssueId]      BIGINT         NOT NULL,
  [CommentId]    BIGINT         NULL,
  [Content]      NVARCHAR(15)   NOT NULL,
  [CreatedAt]    DATETIMEOFFSET NOT NULL
)

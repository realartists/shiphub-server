CREATE TYPE [dbo].[CommentTableType] AS TABLE (
  [Id]           BIGINT         NOT NULL PRIMARY KEY CLUSTERED,
  [IssueId]      BIGINT         NULL, -- Discovered if not provided
  [IssueNumber]  INT            NULL, -- To allow lookup of IssueId
  [UserId]       BIGINT         NOT NULL,
  [Body]         NVARCHAR(MAX)  NOT NULL,
  [CreatedAt]    DATETIMEOFFSET NOT NULL,
  [UpdatedAt]    DATETIMEOFFSET NOT NULL,
  CHECK (IssueId IS NOT NULL OR IssueNumber IS NOT NULL)
)

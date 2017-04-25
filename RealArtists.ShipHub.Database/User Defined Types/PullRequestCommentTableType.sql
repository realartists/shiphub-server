CREATE TYPE [dbo].[PullRequestCommentTableType] AS TABLE (
  [Id]                  BIGINT         NOT NULL PRIMARY KEY CLUSTERED,
  [UserId]              BIGINT         NOT NULL,
  [PullRequestReviewId] BIGINT         NOT NULL,
  [DiffHunk]            NVARCHAR(MAX)  NULL,
  [Path]                NVARCHAR(MAX)  NULL,
  [Position]            BIGINT         NULL,
  [OriginalPosition]    BIGINT         NULL,
  [CommitId]            NVARCHAR(200)  NULL,
  [OriginalCommitId]    NVARCHAR(200)  NULL,
  [Body]                NVARCHAR(MAX)  NOT NULL,
  [CreatedAt]           DATETIMEOFFSET NOT NULL,
  [UpdatedAt]           DATETIMEOFFSET NOT NULL
)

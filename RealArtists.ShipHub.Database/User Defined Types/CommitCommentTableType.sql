CREATE TYPE [dbo].[CommitCommentTableType] AS TABLE (
  [Id]        BIGINT         NOT NULL PRIMARY KEY CLUSTERED,
  [UserId]    BIGINT         NOT NULL,
  [CommitId]  NVARCHAR(200)  NOT NULL,
  [Path]      NVARCHAR(MAX)  NULL,
  [Line]      BIGINT         NULL,
  [Position]  BIGINT         NULL,
  [Body]      NVARCHAR(MAX)  NOT NULL,
  [CreatedAt] DATETIMEOFFSET NOT NULL,
  [UpdatedAt] DATETIMEOFFSET NOT NULL
)

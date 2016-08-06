CREATE TYPE [dbo].[ReactionTableType] AS TABLE (
  [Id]        BIGINT         NOT NULL,
  [UserId]    BIGINT         NOT NULL,
  [Content]   NVARCHAR(15)   NOT NULL,
  [CreatedAt] DATETIMEOFFSET NOT NULL
)

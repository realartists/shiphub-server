CREATE TYPE [dbo].[ReviewTableType] AS TABLE (
  [Id]          BIGINT           NOT NULL PRIMARY KEY CLUSTERED,
  [UserId]      BIGINT           NOT NULL,
  [Body]        NVARCHAR(MAX)    NULL,
  [CommitId]    NVARCHAR(200)    NULL,
  [State]       NVARCHAR(200)    NOT NULL,
  [SubmittedAt] DATETIMEOFFSET   NULL,
  [Hash]        UNIQUEIDENTIFIER NOT NULL
)

CREATE TYPE [dbo].[HookTableType] AS TABLE (
  [Id]             BIGINT           NOT NULL PRIMARY KEY CLUSTERED,
  [GitHubId]       BIGINT           NULL,
  [Secret]         UNIQUEIDENTIFIER NOT NULL,
  [Events]         NVARCHAR(500)    NOT NULL,
  [LastError]      DATETIMEOFFSET   NULL
)

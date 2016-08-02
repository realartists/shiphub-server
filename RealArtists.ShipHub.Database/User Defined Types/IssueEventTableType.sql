CREATE TYPE [dbo].[IssueEventTableType] AS TABLE (
  [Id]            BIGINT           NOT NULL PRIMARY KEY CLUSTERED,
  [IssueId]       BIGINT           NOT NULL,
  [ActorId]       BIGINT           NULL,
  [Event]         NVARCHAR(64)     NOT NULL,
  [CreatedAt]     DATETIMEOFFSET   NOT NULL,
  [Hash]          UNIQUEIDENTIFIER NULL,
  [Restricted]    BIT              NOT NULL,
  [ExtensionData] NVARCHAR(MAX)    NOT NULL
)

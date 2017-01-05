CREATE TYPE [dbo].[IssueEventTableType] AS TABLE (
  [UniqueKey]     NVARCHAR(255)    NOT NULL PRIMARY KEY CLUSTERED,
  [Id]            BIGINT           NULL,
  [IssueId]       BIGINT           NOT NULL,
  [ActorId]       BIGINT           NULL,
  [Event]         NVARCHAR(64)     NOT NULL,
  [CreatedAt]     DATETIMEOFFSET   NOT NULL,
  [Hash]          UNIQUEIDENTIFIER NULL,
  [Restricted]    BIT              NOT NULL,
  [ExtensionData] NVARCHAR(MAX)    NOT NULL
)

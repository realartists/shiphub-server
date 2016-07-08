CREATE TABLE [dbo].[IssueEvents] (
  [Id]            BIGINT         NOT NULL,
  [RepositoryId]  BIGINT         NOT NULL,
  [IssueId]       BIGINT         NOT NULL,
  [ActorId]       BIGINT         NOT NULL,
  [CommitId]      NVARCHAR(40)   NULL,
  [Event]         NVARCHAR(64)   NOT NULL,
  [CreatedAt]     DATETIMEOFFSET NOT NULL,
  [AssigneeId]    BIGINT         NULL,
  [ExtensionData] NVARCHAR(MAX)  NOT NULL,
  CONSTRAINT [PK_IssueEventsEvents] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_IssueEventsEvents_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories] ([Id]),
  -- Don't enforce foreign keys because they may reference deleted items
)
GO

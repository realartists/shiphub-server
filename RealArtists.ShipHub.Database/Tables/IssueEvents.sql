CREATE TABLE [dbo].[IssueEvents] (
  [Id]            BIGINT           NOT NULL,
  [UniqueKey]     NVARCHAR(255)    NOT NULL,
  [RepositoryId]  BIGINT           NOT NULL,
  [IssueId]       BIGINT           NOT NULL,
  [ActorId]       BIGINT           NULL,
  [Event]         NVARCHAR(64)     NOT NULL,
  [CreatedAt]     DATETIMEOFFSET   NOT NULL,
  [Hash]          UNIQUEIDENTIFIER NULL,
  [Restricted]    BIT              NOT NULL,
  [Timeline]      BIT              NOT NULL,
  [ExtensionData] NVARCHAR(MAX)    NOT NULL,
  CONSTRAINT [CK_IssueEvents_Id] CHECK ([Id] != 0),
  CONSTRAINT [PK_IssueEvents] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_IssueEvents_IssueId_Issues_Id] FOREIGN KEY ([IssueId]) REFERENCES [dbo].[Issues] ([Id]),
  CONSTRAINT [FK_IssueEvents_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories] ([Id]),
  CONSTRAINT [FK_IssueEvents_ActorId_Accounts_Id] FOREIGN KEY ([ActorId]) REFERENCES [dbo].[Accounts] ([Id]),
)
GO

CREATE UNIQUE INDEX [UIX_IssueEvents_UniqueKey] ON [dbo].[IssueEvents]([UniqueKey])
GO

CREATE NONCLUSTERED INDEX [IX_IssueEvents_RepositoryId] ON [dbo].[IssueEvents]([RepositoryId])
GO

CREATE NONCLUSTERED INDEX [IX_IssueEvents_IssueId] ON [dbo].[IssueEvents]([IssueId])
GO

CREATE NONCLUSTERED INDEX [IX_IssueEvents_ActorId]
  ON [dbo].[IssueEvents]([ActorId])
  WHERE ([ActorId] IS NOT NULL)
GO

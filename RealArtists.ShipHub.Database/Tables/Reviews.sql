CREATE TABLE [dbo].[Reviews] (
  [Id]           BIGINT           NOT NULL,
  [IssueId]      BIGINT           NOT NULL,
  [RepositoryId] BIGINT           NOT NULL,
  [UserId]       BIGINT           NOT NULL,
  [Body]         NVARCHAR(MAX)    NOT NULL,
  [CommitId]     NVARCHAR(200)    NOT NULL,
  [State]        NVARCHAR(200)    NOT NULL,
  [SubmittedAt]  DATETIMEOFFSET   NULL,
  [Date]         DATETIMEOFFSET   NOT NULL,
  [Hash]         UNIQUEIDENTIFIER NOT NULL,
  CONSTRAINT [PK_Reviews] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_Reviews_IssueId_Issues_Id] FOREIGN KEY ([IssueId]) REFERENCES [dbo].[Issues]([Id]),
  CONSTRAINT [FK_Reviews_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories]([Id]),
  CONSTRAINT [FK_Reviews_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts]([Id])
)
GO

CREATE NONCLUSTERED INDEX [IX_Reviews_IssueId] ON [dbo].[Reviews]([IssueId])
GO

CREATE NONCLUSTERED INDEX [IX_Reviews_RepositoryId] ON [dbo].[Reviews]([RepositoryId])
GO

CREATE NONCLUSTERED INDEX [IX_Reviews_UserId] ON [dbo].[Reviews]([UserId])
GO

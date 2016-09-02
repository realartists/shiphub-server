CREATE TABLE [dbo].[Comments] (
  [Id]                   BIGINT         NOT NULL,
  [IssueId]              BIGINT         NOT NULL,
  [RepositoryId]         BIGINT         NOT NULL,
  [UserId]               BIGINT         NOT NULL,
  [Body]                 NVARCHAR(MAX)  NOT NULL,
  [CreatedAt]            DATETIMEOFFSET NOT NULL,
  [UpdatedAt]            DATETIMEOFFSET NOT NULL,
  [MetadataJson]         NVARCHAR(MAX)  NULL,
  [ReactionMetadataJson] NVARCHAR(MAX)  NULL,
  CONSTRAINT [PK_Comments] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_Comments_IssueId_Issues_Id] FOREIGN KEY ([IssueId]) REFERENCES [dbo].[Issues]([Id]),
  CONSTRAINT [FK_Comments_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories]([Id]),
  CONSTRAINT [FK_Comments_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts]([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_Comments_IssueId] ON [dbo].[Comments]([IssueId])
GO

CREATE NONCLUSTERED INDEX [IX_Comments_RepositoryId] ON [dbo].[Comments]([RepositoryId])
GO

CREATE NONCLUSTERED INDEX [IX_Comments_UserId] ON [dbo].[Comments]([UserId])
GO

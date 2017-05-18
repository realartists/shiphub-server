CREATE TABLE [dbo].[CommitComments] (
  [Id]                   BIGINT         NOT NULL,
  [RepositoryId]         BIGINT         NOT NULL,
  [UserId]               BIGINT         NOT NULL,
  [CommitId]             NVARCHAR(200)  NOT NULL,
  [Path]                 NVARCHAR(MAX)  NULL,
  [Line]                 BIGINT         NULL,
  [Position]             BIGINT         NULL,
  [Body]                 NVARCHAR(MAX)  NOT NULL,
  [CreatedAt]            DATETIMEOFFSET NOT NULL,
  [UpdatedAt]            DATETIMEOFFSET NOT NULL,
  [MetadataJson]         NVARCHAR(MAX)  NULL,
  [ReactionMetadataJson] NVARCHAR(MAX)  NULL,
  CONSTRAINT [PK_CommitComments] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_CommitComments_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories]([Id]),
  CONSTRAINT [FK_CommitComments_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts]([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_CommitComments_RepositoryId] ON [dbo].[CommitComments]([RepositoryId])
GO

CREATE NONCLUSTERED INDEX [IX_CommitComments_UserId] ON [dbo].[CommitComments]([UserId])
GO

CREATE NONCLUSTERED INDEX [IX_CommitComments_CommitId] ON [dbo].[CommitComments]([CommitId])
GO

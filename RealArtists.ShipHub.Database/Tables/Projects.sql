CREATE TABLE [dbo].[Projects]
(
  [Id]              BIGINT            NOT NULL,
  [Name]            NVARCHAR(255)     NOT NULL,
  [Number]          BIGINT            NOT NULL,
  [Body]            NVARCHAR(MAX)     NULL,
  [CreatedAt]       DATETIMEOFFSET    NOT NULL,
  [UpdatedAt]       DATETIMEOFFSET    NOT NULL,
  [CreatorId]       BIGINT            NOT NULL,
  [OrganizationId]  BIGINT            NULL,
  [RepositoryId]    BIGINT            NULL,
  CONSTRAINT [PK_Projects] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_Projects_CreatorId_Accounts_Id] FOREIGN KEY ([CreatorId]) REFERENCES [dbo].[Accounts] ([Id]),
  CONSTRAINT [FK_Projects_OrganizationId_Accounts_Id] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Accounts] ([Id]),
  CONSTRAINT [FK_Projects_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories] ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_Projects_CreatorId] ON [dbo].[Projects]([CreatorId])
GO

CREATE NONCLUSTERED INDEX [IX_Projects_OrganizationId] ON [dbo].[Projects]([OrganizationId])
GO

CREATE NONCLUSTERED INDEX [IX_Projects_RepositoryId] ON [dbo].[Projects]([RepositoryId])
GO

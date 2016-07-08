CREATE TABLE [dbo].[RepositoryLog] (
  [Id]           BIGINT       NOT NULL IDENTITY(1,1),
  [RepositoryId] BIGINT       NOT NULL,
  [Type]         NVARCHAR(20) NOT NULL,
  [ItemId]       BIGINT       NOT NULL,
  [Delete]       BIT          NOT NULL,
  [RowVersion]   BIGINT       NOT NULL CONSTRAINT [DF_RepositoryLog_RowVersion] DEFAULT (NEXT VALUE FOR [dbo].[SyncIdentifier]),
  CONSTRAINT [PK_RepositoryLog] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_RepositoryLog_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories] ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_RepositoryLog_RepositoryId] ON [dbo].[RepositoryLog]([RepositoryId])
GO

CREATE NONCLUSTERED INDEX [IX_RepositoryLog_RepositoryId_Type] ON [dbo].[RepositoryLog]([RepositoryId], [Type])
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_RepositoryLog_Type_ItemId_RepositoryId] ON [dbo].[RepositoryLog]([Type], [ItemId], [RepositoryId])
GO

CREATE NONCLUSTERED INDEX [IX_RepositoryLog_RepositoryId_RowVersion] ON [dbo].[RepositoryLog]([RepositoryId], [RowVersion])
GO

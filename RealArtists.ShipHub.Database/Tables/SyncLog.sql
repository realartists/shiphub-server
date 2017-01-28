CREATE TABLE [dbo].[SyncLog] (
  [RowVersion]     BIGINT       NOT NULL CONSTRAINT [DF_SyncLog_RowVersion] DEFAULT (NEXT VALUE FOR [dbo].[SyncIdentifier]),
  [OwnerType]      NVARCHAR(4)  NOT NULL,
  [OwnerId]        BIGINT       NOT NULL,
  [ItemType]       NVARCHAR(20) NOT NULL,
  [ItemId]         BIGINT       NOT NULL,
  [Delete]         BIT          NOT NULL,
  -- Keep for validation until we're sure things work
  --[OrganizationId] AS IIF([OwnerType] = 'org', [OwnerId], NULL) PERSISTED,
  --[RepositoryId]   AS IIF([OwnerType] = 'repo', [OwnerId], NULL) PERSISTED,
  --CONSTRAINT [FK_SyncLog_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories] ([Id]),
  --CONSTRAINT [FK_SyncLog_OrganizationId_Accounts_Id] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Accounts] ([Id]),
  --CONSTRAINT [CK_SyncLog_ItemType_Delete] CHECK ([Delete] = 0 OR ItemType IN ('comment', 'label', 'milestone', 'project', 'reaction')),
  --CONSTRAINT [CK_SyncLog_OrganizationId_OwnerType] CHECK ([OwnerType] IN ('org', 'repo')),
  --CONSTRAINT [CK_SyncLog_OrganizationId_ItemType] CHECK ([ItemType] IN ('account', 'comment', 'event', 'issue', 'label', 'milestone', 'project', 'reaction', 'repository')),
  -- End validation
  CONSTRAINT [PK_SyncLog] PRIMARY KEY CLUSTERED ([RowVersion]),
  
)
GO

-- Used regularly for inserts
CREATE UNIQUE NONCLUSTERED INDEX [UIX_SyncLog_OwnerType_OwnerId_ItemType_ItemId]
  ON [dbo].[SyncLog]([OwnerType], [OwnerId], [ItemType], [ItemId])
  --INCLUDE (OrganizationId, RepositoryId)
GO

-- Used regularly for updates
CREATE NONCLUSTERED INDEX [IX_SyncLog_ItemType_ItemId]
  ON [dbo].[SyncLog]([ItemType], [ItemId])
  INCLUDE ([OwnerType], [OwnerId], [Delete])--, [OrganizationId], [RepositoryId])
GO

-- Used regularly for sync
CREATE NONCLUSTERED INDEX [IX_SyncLog_OwnerType_OwnerId_RowVersion]
  ON [dbo].[SyncLog]([OwnerType], [OwnerId], [RowVersion])
  INCLUDE([ItemType], [ItemId], [Delete])
GO

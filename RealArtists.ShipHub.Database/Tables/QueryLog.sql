CREATE TABLE [dbo].[QueryLog] (
  [RowVersion] BIGINT           NOT NULL CONSTRAINT [DF_QueryLog_RowVersion] DEFAULT (NEXT VALUE FOR [dbo].[SyncIdentifier]),
  [QueryId]    UNIQUEIDENTIFIER NOT NULL,
  [WatcherId]  BIGINT           NOT NULL,
  [Delete]     BIT              NOT NULL,
  CONSTRAINT [PK_QueryLog] PRIMARY KEY ([RowVersion])
)
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_QueryLog_QueryId_WatcherId]
  ON [dbo].[QueryLog]([QueryId], [WatcherId])
GO

CREATE NONCLUSTERED INDEX [IX_QueryLog_WatcherId_RowVersion]
  ON [dbo].[QueryLog]([WatcherId], [RowVersion])
GO

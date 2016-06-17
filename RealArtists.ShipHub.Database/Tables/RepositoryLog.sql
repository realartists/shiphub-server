CREATE TABLE [dbo].[RepositoryLog] (
  [Id]           BIGINT       NOT NULL IDENTITY(1,1),
  [RepositoryId] BIGINT       NOT NULL,
  [Type]         NVARCHAR(20) NOT NULL,
  [ItemId]       BIGINT       NOT NULL,
  [Delete]       BIT          NOT NULL,
  [RowVersion]   BIGINT       NULL,
  CONSTRAINT [PK_RepositoryLog] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_RepositoryLog_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories] ([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_RepositoryLog_RepositoryId] ON [dbo].[RepositoryLog]([RepositoryId]);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_RepositoryLog_Type_ItemId_RepositoryId] ON [dbo].[RepositoryLog]([Type], [ItemId], [RepositoryId]);
GO

CREATE NONCLUSTERED INDEX [IX_RepositoryLog_RepositoryId_RowVersion] ON [dbo].[RepositoryLog]([RepositoryId], [RowVersion]);
GO

CREATE TRIGGER [dbo].[TRG_RepositoryLog_Version]
ON [dbo].[RepositoryLog]
AFTER INSERT, UPDATE
NOT FOR REPLICATION
AS 
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  UPDATE [RepositoryLog]
    SET [RowVersion] = NEXT VALUE FOR [dbo].[SyncIdentifier]
  WHERE Id IN (SELECT Id FROM inserted)
END
GO

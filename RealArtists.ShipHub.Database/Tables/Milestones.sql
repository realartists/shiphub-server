CREATE TABLE [dbo].[Milestones] (
  [Id]             INT            NOT NULL,
  [RepositoryId]   INT            NOT NULL,
  [UserId]         INT            NOT NULL,
  [Number]         INT            NOT NULL,
  [State]          NVARCHAR(10)   NOT NULL,
  [Title]          NVARCHAR(255)  NOT NULL,
  [Description]    NVARCHAR(255)  NOT NULL,
  [CreatedAt]      DATETIMEOFFSET NOT NULL,
  [UpdatedAt]      DATETIMEOFFSET NOT NULL,
  [ClosedAt]       DATETIMEOFFSET NULL,
  [DueOn]          DATETIMEOFFSET NULL,
  [MetaDataId]     BIGINT         NULL,
  [RowVersion]     BIGINT         NULL,
  CONSTRAINT [PK_Milestones] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_Milestones_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories]([Id]),
  CONSTRAINT [FK_Milestones_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts]([Id]),
  CONSTRAINT [FK_Milestones_MetaDataId_GitHubMetaData_Id] FOREIGN KEY ([MetaDataId]) REFERENCES [dbo].[GitHubMetaData]([Id]),
);
GO

CREATE NONCLUSTERED INDEX [IX_Milestones_RowVersion] ON [dbo].[Milestones]([RowVersion]);
GO

CREATE NONCLUSTERED INDEX [IX_Milestones_RepositoryId] ON [dbo].[Milestones]([RepositoryId]);
GO

CREATE NONCLUSTERED INDEX [IX_Milestones_UserId] ON [dbo].[Milestones]([UserId]);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Milestones_MetaDataId]
  ON [dbo].[Milestones]([MetaDataId])
  WHERE ([MetaDataId] IS NOT NULL);
GO

CREATE TRIGGER [dbo].[TRG_Milestones_Version]
ON [dbo].[Milestones]
AFTER INSERT, UPDATE
NOT FOR REPLICATION
AS 
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  UPDATE Milestones SET
    [RowVersion] = NEXT VALUE FOR [dbo].[SyncIdentifier]
  WHERE Id IN (SELECT Id FROM inserted)
END
GO
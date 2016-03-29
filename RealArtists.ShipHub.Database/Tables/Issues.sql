CREATE TABLE [dbo].[Issues] (
  [Id]           INT NOT NULL,
  [UserId]       INT NOT NULL,
  [RepositoryId] INT NOT NULL,
  [Number]       INT NOT NULL,
  [State]        NVARCHAR(6) NOT NULL,
  [Title]        NVARCHAR(255) NOT NULL,
  [Body]         NVARCHAR(MAX) NOT NULL,
  [AssigneeId]   INT NULL,
  [MilestoneId]  INT NULL,
  [Locked]       BIT NOT NULL,
  [CreatedAt]    DATETIMEOFFSET NOT NULL,
  [UpdatedAt]    DATETIMEOFFSET NOT NULL,
  [ClosedAt]     DATETIMEOFFSET NULL,
  [ClosedById]   INT NULL,
  [MetaDataId]   BIGINT NULL,
  [RowVersion]   BIGINT NULL,
  CONSTRAINT [PK_Issues] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_Issues_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts] ([Id]),
  CONSTRAINT [FK_Issues_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories] ([Id]),
  CONSTRAINT [FK_Issues_MilestoneId_Milestones_Id] FOREIGN KEY ([MilestoneId]) REFERENCES [dbo].[Milestones] ([Id]),
  CONSTRAINT [FK_Issues_AssigneeId_Accounts_Id] FOREIGN KEY ([AssigneeId]) REFERENCES [dbo].[Accounts] ([Id]),
  CONSTRAINT [FK_Issues_ClosedById_Accounts_Id] FOREIGN KEY ([ClosedById]) REFERENCES [dbo].[Accounts] ([Id]),
  CONSTRAINT [FKSN_Issues_MetaDataId_GitHubMetaData_Id] FOREIGN KEY ([MetaDataId]) REFERENCES [dbo].[GitHubMetaData]([Id]) ON DELETE SET NULL,
);
GO

CREATE NONCLUSTERED INDEX [IX_Issues_UserId] ON [dbo].[Issues]([UserId]);
GO

CREATE NONCLUSTERED INDEX [IX_Issues_RepositoryId] ON [dbo].[Issues]([RepositoryId]);
GO

CREATE NONCLUSTERED INDEX [IX_Issues_MilestoneId] ON [dbo].[Issues]([MilestoneId]);
GO

CREATE NONCLUSTERED INDEX [IX_Issues_ClosedById] ON [dbo].[Issues]([ClosedById]);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Issues_MetaDataId] ON [dbo].[Issues]([MetaDataId]);
GO

CREATE NONCLUSTERED INDEX [IX_Issues_RowVersion] ON [dbo].[Issues]([RowVersion]);
GO

CREATE TRIGGER [dbo].[TRG_Issues_Version]
ON [dbo].[Issues]
AFTER INSERT, UPDATE
NOT FOR REPLICATION
AS 
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

   UPDATE Issues SET
    [RowVersion] = NEXT VALUE FOR [dbo].[SyncIdentifier]
  WHERE Id IN (SELECT Id FROM inserted)
END
GO

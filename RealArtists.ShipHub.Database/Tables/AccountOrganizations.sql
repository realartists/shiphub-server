CREATE TABLE [dbo].[AccountOrganizations] (
  [UserId]         INT NOT NULL,
  [OrganizationId] INT NOT NULL,
  CONSTRAINT [PK_AccountOrganizations] PRIMARY KEY CLUSTERED ([UserId], [OrganizationId]),
  CONSTRAINT [FK_AccountOrganizations_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts] ([Id]),
  CONSTRAINT [FK_AccountOrganizations_OrganizationId_Accounts_Id] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Accounts] ([Id]),
);
GO

CREATE NONCLUSTERED INDEX [IX_AccountOrganizations_UserId] ON [dbo].[AccountOrganizations]([UserId]);
GO

CREATE NONCLUSTERED INDEX [IX_AccountOrganizations_OrganizationId] ON [dbo].[AccountOrganizations]([OrganizationId]);
GO

CREATE TRIGGER [dbo].[TRG_AccountOrganizations_Version]
ON [dbo].[AccountOrganizations]
AFTER INSERT, UPDATE, DELETE
NOT FOR REPLICATION
AS 
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  UPDATE Accounts
    SET [RowVersion] = NEXT VALUE FOR [dbo].[SyncIdentifier]
    WHERE Id IN (
      SELECT OrganizationId FROM inserted
      UNION
      SELECT OrganizationId FROM deleted
      UNION
      SELECT UserId FROM inserted)
END
GO

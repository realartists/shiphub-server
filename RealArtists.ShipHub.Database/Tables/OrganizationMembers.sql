CREATE TABLE [dbo].[OrganizationMembers] (
  [OrganizationId] INT    NOT NULL,
  [UserId]         INT    NOT NULL,
  CONSTRAINT [PK_OrganizationMembers] PRIMARY KEY CLUSTERED ([OrganizationId], [UserId] ASC),
  CONSTRAINT [FKCD_OrganizationMembers_OrganizationId_Accounts_Id] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Accounts] ([Id]) ON DELETE CASCADE,
  CONSTRAINT [FK_OrganizationMembers_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts] ([Id]),
);
GO

CREATE TRIGGER [dbo].[TRG_OrganizationMembers_Version]
ON [dbo].[OrganizationMembers]
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
      SELECT OrganizationId FROM deleted)
END
GO

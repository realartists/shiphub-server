CREATE TABLE [dbo].[OrganizationMembers] (
  [OrganizationId] INT    NOT NULL,
  [UserId]         INT    NOT NULL,
  [Deleted]        BIT    NOT NULL,
  [RowVersion]     BIGINT NULL,
  [RestoreVersion] BIGINT NULL,
  CONSTRAINT [PK_OrganizationMembers] PRIMARY KEY CLUSTERED ([OrganizationId], [UserId] ASC),
  CONSTRAINT [FKCD_OrganizationMembers_OrganizationId_Accounts_Id] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Accounts] ([Id]) ON DELETE CASCADE,
  CONSTRAINT [FK_OrganizationMembers_UserId_Accounts_Id] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Accounts] ([Id]),
);
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_OrganizationMembers_RowVersion] ON [dbo].[OrganizationMembers]([RowVersion]);
GO

CREATE TRIGGER [dbo].[TRG_OrganizationMembers_Version]
ON [dbo].[OrganizationMembers]
AFTER INSERT, UPDATE
NOT FOR REPLICATION
AS 
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  DECLARE @Versions TABLE (
    [OrganizationId] INT    NOT NULL,
    [UserId]         INT    NOT NULL,
    [RowVersion]     BIGINT NULL,
    PRIMARY KEY CLUSTERED ([OrganizationId], [UserId] ASC));

  INSERT INTO @Versions
  SELECT i.OrganizationId, i.UserId, IIF(i.[RowVersion] IS NULL, i.RestoreVersion, NULL)
  FROM inserted as i

  UPDATE @Versions SET
    [RowVersion] = NEXT VALUE FOR [dbo].[SyncIdentifier]
  WHERE [RowVersion] IS NULL

  UPDATE OrganizationMembers SET
    [RowVersion] = v.[RowVersion],
    [RestoreVersion] = NULL
  FROM OrganizationMembers as t
    INNER JOIN @Versions as v ON (v.Id = t.Id)
END
GO
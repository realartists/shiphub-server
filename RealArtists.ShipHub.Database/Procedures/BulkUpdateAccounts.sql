CREATE PROCEDURE [dbo].[BulkUpdateAccounts]
  @Date DATETIMEOFFSET,
  @Accounts AccountTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to repo log
  DECLARE @Changes TABLE (
    [Id]   BIGINT      NOT NULL PRIMARY KEY CLUSTERED,
    [Type] NVARCHAR(4) NOT NULL
  )

  -- For sync events
  DECLARE @Updates TABLE (
    [OrganizationId] BIGINT NULL,
    [RepositoryId]   BIGINT NULL
  )

  MERGE INTO Accounts WITH (UPDLOCK SERIALIZABLE) as [Target]
  USING (
    SELECT Id, [Type], [Login]
    FROM @Accounts
  ) as [Source]
  ON ([Target].Id = [Source].Id)
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, [Type], [Login], [Date])
    VALUES (Id, [Type], [Login], @Date)
  -- Update
  WHEN MATCHED 
    AND [Target].[Date] < @Date
    AND EXISTS (
      SELECT [Target].[Type], [Target].[Login]
      EXCEPT
      SELECT [Source].[Type], [Source].[Login]
    ) THEN
    UPDATE SET
      [Type] = [Source].[Type],
      [Login] = [Source].[Login],
      [Date] = @Date
  OUTPUT INSERTED.Id, INSERTED.[Type] INTO @Changes (Id, [Type]);

  -- New Organizations reference themselves
  INSERT INTO OrganizationLog WITH (UPDLOCK SERIALIZABLE) (OrganizationId, AccountId, [Delete])
  OUTPUT INSERTED.OrganizationId INTO @Updates (OrganizationId)
  SELECT c.Id, c.Id, 0
  FROM @Changes as c
  WHERE c.[Type] = 'org'
    AND NOT EXISTS (SELECT * FROM OrganizationLog WHERE OrganizationId = c.Id AND AccountId = c.Id)

  -- Other actions manage adding user references to repos.
  -- Our only job here is to mark still valid references as changed.
  UPDATE RepositoryLog WITH (UPDLOCK SERIALIZABLE) SET
    [RowVersion] = DEFAULT -- Bump version
  OUTPUT INSERTED.RepositoryId INTO @Updates (RepositoryId)
  WHERE [Type] = 'account'
    AND [Delete] = 0
    AND ItemId IN (SELECT Id FROM @Changes)
    AND ItemId != 10137 -- Ghost user (present in most repos. Do not ever mark as updated.)

  -- Return updated organizations and repositories
  SELECT DISTINCT OrganizationId, RepositoryId, NULL as UserId FROM @Updates
END

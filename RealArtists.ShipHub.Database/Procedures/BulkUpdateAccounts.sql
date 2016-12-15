CREATE PROCEDURE [dbo].[BulkUpdateAccounts]
  @Date DATETIMEOFFSET,
  @Accounts AccountTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to sync log
  DECLARE @Changes TABLE (
    [Id]   BIGINT      NOT NULL PRIMARY KEY CLUSTERED,
    [Type] NVARCHAR(4) NOT NULL
  )

  BEGIN TRY
    BEGIN TRANSACTION

    MERGE INTO Accounts as [Target]
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

    -- Ensuring organizations reference themselves is handled by
    -- [SetUserOrganizations]

    -- Other actions manage adding user references to repos.
    -- Our only job here is to mark still valid references as changed.
    UPDATE SyncLog SET
      [RowVersion] = DEFAULT -- Bump version
    OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
    WHERE [ItemType] = 'account'
      AND ItemId IN (SELECT Id FROM @Changes)
      AND ItemId != 10137 -- Ghost user (present in most repos. Do not ever mark as updated.)

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END

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
    [Id] BIGINT NOT NULL PRIMARY KEY CLUSTERED
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
    OUTPUT INSERTED.Id INTO @Changes;

    -- Ensuring organizations reference themselves is handled by
    -- [SetUserOrganizations]

    -- Other actions manage adding user references to repos.
    -- Our only job here is to mark still valid references as changed.
    -- The LOOP JOIN and FORCE ORDER prevent a scan and merge which deadlocks on PK_SyncLog
    UPDATE SyncLog SET
      [RowVersion] = DEFAULT -- Bump version
    OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
    FROM @Changes as c
      INNER LOOP JOIN SyncLog as sl ON (sl.ItemType = 'account' AND sl.ItemId = c.Id)
    WHERE sl.ItemId != 10137 -- Ghost user (present in most repos. Do not ever mark as updated.)
    OPTION (FORCE ORDER)

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END

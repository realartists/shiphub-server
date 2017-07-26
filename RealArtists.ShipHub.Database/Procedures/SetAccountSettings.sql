CREATE PROCEDURE [dbo].[SetAccountSettings]
  @AccountId BIGINT,
  @SyncSettingsJson NVARCHAR(MAX)
  AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  BEGIN TRY
    BEGIN TRANSACTION

    MERGE INTO AccountSettings WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT @AccountId as AccountId, @SyncSettingsJson as SyncSettingsJson
    ) as [Source]
    ON ([Target].AccountId = [Source].AccountId)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (AccountId, SyncSettingsJson)
      VALUES (AccountId, SyncSettingsJson)
   -- Update
    WHEN MATCHED THEN UPDATE SET
      SyncSettingsJson = [Source].SyncSettingsJson;

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END

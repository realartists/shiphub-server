CREATE PROCEDURE [dbo].[BulkUpdateSubscriptions]
  @Subscriptions SubscriptionTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to sync log
  DECLARE @Changes TABLE (
    [Id] BIGINT NOT NULL PRIMARY KEY CLUSTERED,
    [StateChanged] BIT NOT NULL,
    [TrialChanged] BIT NOT NULL
  )

  BEGIN TRY
    BEGIN TRANSACTION

    MERGE INTO Subscriptions WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT AccountId, [State], TrialEndDate, [Version]
      FROM @Subscriptions
    ) as [Source]
    ON ([Target].AccountId = [Source].AccountId)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (AccountId, [State], TrialEndDate, [Version])
      VALUES (AccountId, [State], TrialEndDate, [Version])
    -- Update
    WHEN MATCHED AND [Target].[Version] < [Source].[Version]
      THEN UPDATE SET
        [State] = [Source].[State],
        TrialEndDate = [Source].TrialEndDate,
        [Version] = [Source].[Version]
    OUTPUT INSERTED.AccountId,
           IIF(INSERTED.[State] != ISNULL(DELETED.[State], ''), 1, 0),
           IIF(ISNULL(INSERTED.TrialEndDate, '') != ISNULL(DELETED.TrialEndDate, ''), 1, 0) INTO @Changes
    OPTION (LOOP JOIN, FORCE ORDER);

    SELECT a.[Type] as ItemType, a.Id as ItemId
    FROM @Changes as c
      INNER JOIN Accounts as a ON (a.Id = c.Id)
    WHERE c.StateChanged = 1 OR c.TrialChanged = 1
    OPTION (LOOP JOIN, FORCE ORDER)

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END

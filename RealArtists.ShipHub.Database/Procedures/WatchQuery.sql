CREATE PROCEDURE [dbo].[WatchQuery]
  @Id UNIQUEIDENTIFIER,
  @WatcherId BIGINT,
  @Watch BIT
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  IF (NOT EXISTS (SELECT * FROM Queries WHERE Id = @Id))
  BEGIN
    RETURN
  END

  BEGIN TRY
    BEGIN TRANSACTION

    -- updating watch
    UPDATE QueryLog SET 
      [RowVersion] = DEFAULT,
      [Delete] = CASE WHEN @Watch = 0 THEN 1 ELSE 0 END
    WHERE WatcherId = @WatcherId AND QueryId = @Id

    IF(@@ROWCOUNT = 0 AND @Watch = 1)
    BEGIN
      -- inserting new watch
      INSERT INTO QueryLog (QueryId, WatcherId, [Delete])
      SELECT @Id, @WatcherId, 0
      WHERE NOT EXISTS (SELECT * FROM QueryLog WHERE WatcherId = @WatcherId AND QueryId = @Id) 
    END

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH

  -- Return changelog
  SELECT 'user' AS ItemType, @WatcherId as ItemId
END

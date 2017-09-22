CREATE PROCEDURE [dbo].[WatchQuery]
  @Id UNIQUEIDENTIFIER,
  @WatcherId BIGINT,
  @Watch BIT
AS
BEGIN
  SET NOCOUNT ON

   BEGIN TRY
    BEGIN TRANSACTION
      -- removing watch
      UPDATE QueryLog SET 
        [RowVersion] = Default,
        [Delete] = 1
      WHERE WatcherId = @WatcherId AND QueryId = @Id AND [Delete] = 0 AND @Watch = 0;
      
      -- inserting new watch
      INSERT INTO QueryLog (QueryId, WatcherId, [Delete])
      SELECT @Id, @WatcherId, 0
      WHERE EXISTS(SELECT * FROM Queries WHERE Id = @Id) 
        AND NOT EXISTS (SELECT * FROM QueryLog WHERE WatcherId = @WatcherId AND QueryId = @Id) 
        AND @Watch = 1;

      -- toggling watch from off to on
      UPDATE QueryLog SET 
        [RowVersion] = Default,
        [Delete] = 0
      WHERE WatcherId = @WatcherId AND QueryId = @Id AND [Delete] = 1 AND @Watch = 1;

    COMMIT TRANSACTION
   END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH

  -- Return changelog
  SELECT 'user' AS ItemType, @WatcherId as ItemId;

END

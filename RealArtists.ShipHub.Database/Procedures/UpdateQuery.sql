CREATE PROCEDURE [dbo].[UpdateQuery]
  @Id UNIQUEIDENTIFIER,
  @AuthorId BIGINT,
  @Title NVARCHAR(255),
  @Predicate NVARCHAR(MAX)
AS
BEGIN
  SET NOCOUNT ON

  BEGIN TRY
    BEGIN TRANSACTION

      MERGE INTO Queries WITH (SERIALIZABLE) as [Target]
      USING (
        SELECT @Id as Id
      ) AS [Source]
      ON ([Target].Id = [Source].Id)
      -- Add
      WHEN NOT MATCHED BY TARGET THEN
        INSERT (Id, Title, AuthorId, [Predicate])
        VALUES (Id, @Title, @AuthorId, @Predicate)
      WHEN MATCHED AND ([Target].AuthorId = @AuthorId) THEN
        UPDATE SET
          Title = @Title,
          [Predicate] = @Predicate;
      -- WHEN AuthorId mismatch, we refuse to do anything

      -- If we've failed to change anything, this is due to an AuthorId mismatch. Abort.
      IF (@@ROWCOUNT = 0)
      BEGIN
        RETURN
      END

      -- Let the author of the query start watching it again if she had previously stopped watching
      UPDATE QueryLog SET
        [Delete] = 0
      WHERE WatcherId = @AuthorId
        AND QueryId = @Id
        AND [Delete] = 1;

      -- Update the version in SyncLog for all existing watchers of this query
      UPDATE QueryLog SET
        [RowVersion] = DEFAULT
      OUTPUT 'user' as ItemType, Inserted.WatcherId as ItemId
      WHERE QueryId = @Id
        AND [Delete] = 0;

      -- Insert a watcher row into the synclog for the author if there isn't one already
      INSERT INTO QueryLog (QueryId, WatcherId, [Delete])
      OUTPUT 'user' as ItemType, Inserted.WatcherId as ItemId
      SELECT @Id, @AuthorId, 0
       WHERE NOT EXISTS (SELECT * FROM QueryLog WHERE WatcherId = @AuthorId AND QueryId = @Id);

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END
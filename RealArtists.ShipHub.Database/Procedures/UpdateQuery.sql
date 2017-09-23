CREATE PROCEDURE [dbo].[UpdateQuery]
  @Id UNIQUEIDENTIFIER,
  @AuthorId BIGINT,
  @Title NVARCHAR(255),
  @Predicate NVARCHAR(MAX)
AS
BEGIN
  SET NOCOUNT ON

  DECLARE @Watchers TABLE (
    [AccountId] BIGINT NOT NULL PRIMARY KEY
  );

  DECLARE @Changed TABLE (
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY
  );

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
          [Predicate] = @Predicate
      -- WHEN AuthorId mismatch, we refuse to do anything
      OUTPUT Inserted.Id INTO @Changed;

      -- If we've failed to change anything, this is due to an AuthorId mismatch. Raise an error and refuse to do anything further.
      IF (NOT EXISTS (SELECT * FROM @Changed))
        RAISERROR('AuthorId mismatch', 1, 0)

      -- Let the author of the query start watching it again if she had previously stopped watching
      UPDATE QueryLog SET
        [Delete] = 0
      WHERE WatcherId = @AuthorId
        AND QueryId = @Id;

      -- Update the version in SyncLog for all existing watchers of this query
      UPDATE QueryLog SET
        [RowVersion] = DEFAULT
      OUTPUT Inserted.WatcherId INTO @Watchers
      WHERE QueryId = @Id
        AND [Delete] = 0;

      -- Insert a watcher row into the synclog for the author if there isn't one already
      INSERT INTO QueryLog (QueryId, WatcherId, [Delete])
      OUTPUT Inserted.WatcherId INTO @Watchers
      SELECT @Id, @AuthorId, 0
       WHERE NOT EXISTS (SELECT * FROM QueryLog WHERE WatcherId = @AuthorId AND QueryId = @Id);

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH

  -- trigger sync for every user who is watching this query
  SELECT 'user' AS ItemType, AccountId as ItemId
    FROM @Watchers
END
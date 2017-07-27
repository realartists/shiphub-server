CREATE PROCEDURE [dbo].[UpdateAccountSyncRepositories]
  @AccountId BIGINT,
  @AutoTrack BIT,
  @Include StringMappingTableType READONLY,
  @Exclude ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  BEGIN TRY
    BEGIN TRANSACTION

    -- I don't love it, but the best solution I've come up with to update this
    -- is to compute the latest, complete, correct list, and MERGE to apply it.

    DECLARE @SyncRepos TABLE (
      [RepositoryId]     BIGINT        NOT NULL PRIMARY KEY CLUSTERED,
      [RepoMetadataJson] NVARCHAR(MAX) NULL,
      [ShouldAdd]        BIT           NOT NULL
    )
    
    -- This list can safely include things to which the user has lost access
    -- (public repos gone private, removed as contributor) since WhatsNew
    -- enforces the final access checks. Ideally it should be kept in sync with
    -- the user's access rights.

    -- This is kind of subtle and complicated.
    -- First take the user's linked repositories (they have access).
    INSERT INTO @SyncRepos (RepositoryId, RepoMetadataJson, ShouldAdd)
    SELECT RepositoryId, NULL, @AutoTrack
    FROM AccountRepositories
    WHERE AccountId = @AccountId

    -- Then add included, public repositories we know about.
    -- If a link repo is explicitly included, mark it as ShouldAdd
    MERGE INTO @SyncRepos as [Target]
    USING (
      -- Use the join to exlcude unknown repositories.
      SELECT i.[Key] as RepositoryId, i.[Value] as RepoMetadataJson
      FROM @Include as i
        INNER LOOP JOIN Repositories as r ON (r.Id = i.[Key])
      WHERE r.[Private] = 0
    ) as [Source]
    ON [Target].RepositoryId = [Source].RepositoryId
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (RepositoryId, RepoMetadataJson, ShouldAdd)
      VALUES (RepositoryId, RepoMetadataJson, 1)
    WHEN MATCHED THEN
      UPDATE SET
        RepoMetadataJson = [Source].RepoMetadataJson,
        ShouldAdd = 1
    OPTION (LOOP JOIN, FORCE ORDER);

    -- Later, remove excluded repositories.
    DELETE FROM @SyncRepos
    WHERE EXISTS (SELECT * FROM @Exclude WHERE Item = RepositoryId) 

    MERGE INTO AccountSyncRepositories WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT @AccountId as AccountId, RepositoryId, RepoMetadataJson, ShouldAdd
      FROM @SyncRepos
    ) as [Source]
    ON [Target].AccountId = [Source].AccountId
      AND [Target].RepositoryId = [Source].RepositoryId
    -- Add
    WHEN NOT MATCHED BY TARGET AND [Source].ShouldAdd = 1 THEN
      INSERT (AccountId, RepositoryId, RepoMetadataJson)
      VALUES (AccountId, RepositoryId, RepoMetadataJson)
    -- Delete
    WHEN NOT MATCHED BY SOURCE THEN DELETE
    -- UPDATE
    WHEN MATCHED AND [Source].RepoMetadataJson IS NOT NULL THEN
      UPDATE SET
        RepoMetadataJson = [Source].RepoMetadataJson
    OUTPUT 'user' as ItemType, @AccountId as ItemId
    OPTION (LOOP JOIN, FORCE ORDER);

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END

CREATE PROCEDURE [dbo].[UpdateAccountSyncRepositories]
  @AccountId BIGINT,
  @AutoTrack BIT, -- Set to true if prefs unset
  @Include StringMappingTableType READONLY, -- Only include repos that the user can read (pull)
  @Exclude ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  BEGIN TRY
    BEGIN TRANSACTION

    -- def syncRepos(userRepos, prefs):
    --   if prefs is None:
    --     return [r for r in userRepos if r.has_issues and r.permissions.push]
    --   elif prefs.autoTrack:
    --     autoTrackedRepos = [r for r in userRepos if r.has_issues and r.permissions.push]
    --     includedRepos = [r for r in prefs.include if r.permissions.pull]
    --     return (autoTrackedRepos + includedRepos) - prefs.exclude
    --   else: # autoTrack = false
    --     return [r for r in prefs.include if r.permissions.pull]

    -- I don't love it, but the best solution I've come up with to update this
    -- is to compute the latest, complete, correct list, and MERGE to apply it.

    DECLARE @SyncRepos TABLE (
      [RepositoryId]     BIGINT        NOT NULL PRIMARY KEY CLUSTERED,
      [RepoMetadataJson] NVARCHAR(MAX) NULL,
      [ShouldInclude]    BIT           NOT NULL
    )

    -- This is kind of subtle and complicated.
    -- First take the user's linked repositories (they have access).
    -- Keep even repos without issues in this list.
    INSERT INTO @SyncRepos (RepositoryId, RepoMetadataJson, ShouldInclude)
    SELECT ar.RepositoryId, NULL, CASE
      WHEN @AutoTrack = 1 AND r.HasIssues = 1 AND ar.Push = 1 THEN 1
      ELSE 0
    END
    FROM AccountRepositories as ar
      INNER LOOP JOIN Repositories as r ON (r.Id = ar.RepositoryId)
    WHERE ar.AccountId = @AccountId
    OPTION (FORCE ORDER)

    -- Ok, now we have all the user's linked repos, as well as as flag
    -- to help us determine if we should add or remove matches
    -- Now  add included, public repositories we know about.
    -- If a linked repo is explicitly included, mark it as ShouldInclude
    MERGE INTO @SyncRepos as [Target]
    USING (
      -- Use the join to exclude unknown repositories.
      SELECT i.[Key] as RepositoryId, i.[Value] as RepoMetadataJson, r.[Private]
      FROM @Include as i
        INNER LOOP JOIN Repositories as r ON (r.Id = i.[Key])
    ) as [Source]
    ON [Target].RepositoryId = [Source].RepositoryId
    -- Repos not in /user/repos (require they be public)
    WHEN NOT MATCHED BY TARGET AND [Source].[Private] = 0 THEN
      INSERT (RepositoryId, RepoMetadataJson, ShouldInclude)
      VALUES (RepositoryId, RepoMetadataJson, 1)
    -- Repos in /user/repos (mark for addition, leave metadata null)
    WHEN MATCHED THEN
      UPDATE SET ShouldInclude = 1
    OPTION (LOOP JOIN, FORCE ORDER);

    -- Remove excluded and inelegible repositories.
    DELETE FROM @SyncRepos
    WHERE ShouldInclude = 0
      OR EXISTS (SELECT * FROM @Exclude WHERE Item = RepositoryId) 

    MERGE INTO AccountSyncRepositories WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT @AccountId as AccountId, RepositoryId, RepoMetadataJson
      FROM @SyncRepos
    ) as [Source]
    ON [Target].AccountId = [Source].AccountId
      AND [Target].RepositoryId = [Source].RepositoryId
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (AccountId, RepositoryId, RepoMetadataJson)
      VALUES (AccountId, RepositoryId, RepoMetadataJson)
    -- Delete
    WHEN NOT MATCHED BY SOURCE AND [Target].AccountId = @AccountId THEN DELETE
    -- UPDATE
    WHEN MATCHED THEN
      UPDATE SET RepoMetadataJson = [Source].RepoMetadataJson
    OPTION (LOOP JOIN, FORCE ORDER);

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH

  -- Return final list
  SELECT RepositoryId, RepoMetadataJson
  FROM AccountSyncRepositories
  WHERE AccountId = @AccountId
END

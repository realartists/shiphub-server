CREATE PROCEDURE [dbo].[SetGitHubInstallationRepositories]
  @InstallationId BIGINT,
  @RepoIds ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  BEGIN TRY
    BEGIN TRANSACTION

    DELETE FROM GitHubInstallationRepositories
    OUTPUT 'repo' as ItemType, DELETED.RepositoryId as ItemId
    FROM GitHubInstallationRepositories as gir
      LEFT OUTER JOIN @RepoIds as r ON (r.Item = gir.RepositoryId)
    WHERE gir.InstallationId = @InstallationId
      AND r.Item IS NULL
    OPTION (FORCE ORDER)

    MERGE INTO GitHubInstallationRepositories WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT @InstallationId as InstallationId, Item as RepositoryId
        FROM @RepoIds
    ) as [Source]
    ON [Target].InstallationId = [Source].InstallationId
      AND [Target].RepositoryId = [Source].RepositoryId
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (InstallationId, RepositoryId)
      VALUES (InstallationId, RepositoryId)
    OUTPUT 'repo' as ItemType, INSERTED.RepositoryId as ItemId
    OPTION (LOOP JOIN, FORCE ORDER);

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END

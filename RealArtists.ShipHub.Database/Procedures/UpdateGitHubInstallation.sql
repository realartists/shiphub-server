CREATE PROCEDURE [dbo].[UpdateGitHubInstallation]
  @InstallationId BIGINT,
  @AccountId BIGINT,
  @RepositorySelection NVARCHAR(20)
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  BEGIN TRY
    BEGIN TRANSACTION

    MERGE INTO GitHubInstallations WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT @InstallationId AS Id, @AccountId as AccountId, @RepositorySelection AS RepositorySelection
    ) AS [Source]
    ON ([Target].Id = [Source].Id)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (Id, AccountId, RepositorySelection)
      VALUES (Id, AccountId, RepositorySelection)
    -- Update 
    WHEN MATCHED AND [Target].RepositorySelection != [Source].RepositorySelection THEN
      UPDATE SET
        RepositorySelection = [Source].RepositorySelection
    OPTION (LOOP JOIN, FORCE ORDER);

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END

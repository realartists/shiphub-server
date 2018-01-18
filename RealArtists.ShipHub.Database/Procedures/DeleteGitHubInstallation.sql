CREATE PROCEDURE [dbo].[DeleteGitHubInstallation]
  @InstallationId BIGINT
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DECLARE @Changes TABLE (
    [AccountId] BIGINT NOT NULL PRIMARY KEY CLUSTERED
  )

  BEGIN TRY
    BEGIN TRANSACTION

    DELETE FROM GitHubInstallationRepositories
    OUTPUT 'repo' as ItemType, DELETED.RepositoryId as ItemId
    WHERE InstallationId = @InstallationId

    DELETE FROM GitHubInstallations
    OUTPUT DELETED.AccountId INTO @Changes
    WHERE Id = @InstallationId

    -- Changes
    SELECT a.[Type] as ItemType, a.Id as ItemId
    FROM Accounts as a
      INNER LOOP JOIN @Changes as c ON (c.AccountId = a.Id)
    OPTION (FORCE ORDER)

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END


CREATE PROCEDURE [dbo].[SetRepositoryIssueTemplate]
	@RepositoryId BIGINT,
	@IssueTemplate NVARCHAR(MAX) NULL
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DECLARE @Changes TABLE (
	RepositoryId BIGINT NOT NULL
  );

  -- Update the Repository with the new IssueTemplate
  UPDATE Repositories WITH (UPDLOCK SERIALIZABLE)
  SET IssueTemplate = @IssueTemplate
  OUTPUT inserted.Id INTO @Changes
  WHERE Id = @RepositoryId AND ISNULL(IssueTemplate, '') <> ISNULL(@IssueTemplate, '')

  -- Update the log with any changes
  UPDATE RepositoryLog WITH (UPDLOCK SERIALIZABLE) SET
    [RowVersion] = DEFAULT
  FROM RepositoryLog as rl
    INNER JOIN @Changes as c ON (rl.[Type] = 'repository' AND rl.ItemId = c.RepositoryId)

  -- Return whether or not we updated anything
  SELECT NULL as OrganizationId, RepositoryId, NULL as UserId
  FROM @Changes;
END

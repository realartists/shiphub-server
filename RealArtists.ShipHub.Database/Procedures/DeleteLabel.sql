CREATE PROCEDURE [dbo].[DeleteLabel]
  @RepositoryId BIGINT,
  @LabelId BIGINT
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DECLARE @Changes BIT = 0

  DELETE FROM IssueLabels WHERE LabelId = @LabelId
  IF (@@ROWCOUNT > 0)
    SET @Changes = 1

  DELETE FROM Labels WHERE Id = @LabelId
  IF (@@ROWCOUNT > 0)
    SET @Changes = 1

  IF (@Changes > 0)
  BEGIN
    -- Update repo log entry
    UPDATE RepositoryLog WITH (UPDLOCK SERIALIZABLE)
      SET [RowVersion] = DEFAULT
    WHERE RepositoryId = @RepositoryId
      AND [Type] = 'repository'
  END
  
  -- Return updated organizations and repositories
  SELECT NULL as OrganizationId, @RepositoryId as RepositoryId, NULL as UserId WHERE @Changes = 1
END

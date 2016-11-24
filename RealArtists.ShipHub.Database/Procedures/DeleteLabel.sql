CREATE PROCEDURE [dbo].[DeleteLabel]
  @LabelId BIGINT
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DELETE FROM IssueLabels WHERE LabelId = @LabelId

  DELETE FROM Labels WHERE Id = @LabelId

  UPDATE RepositoryLog WITH (UPDLOCK SERIALIZABLE) SET
    [Delete] = 1,
    [RowVersion] = DEFAULT
  OUTPUT NULL as OrganizationId, INSERTED.RepositoryId, NULL as UserId
  WHERE [Type] = 'label'
    AND ItemId = @LabelId
    AND [Delete] = 0
END

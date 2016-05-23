CREATE PROCEDURE [dbo].[SetRepositoryAssignableAccounts]
  @RepositoryId BIGINT,
  @AssignableAccountIds ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  MERGE INTO RepositoryAccounts as Target
  USING (
    SELECT Item as AccountId, @RepositoryId as RepositoryId
      FROM @AssignableAccountIds) as Source
  ON Target.AccountId = Source.AccountId
    AND Target.RepositoryId = Source.RepositoryId
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (AccountId, RepositoryId) VALUES (AccountId, RepositoryId)
  WHEN NOT MATCHED BY SOURCE
    AND Target.RepositoryId = @RepositoryId
    THEN DELETE;
END

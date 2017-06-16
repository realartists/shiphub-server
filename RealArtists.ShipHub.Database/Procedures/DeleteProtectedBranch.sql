CREATE PROCEDURE [dbo].[DeleteProtectedBranch]
  @RepositoryId BIGINT,
  @Name NVARCHAR(MAX)
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DECLARE @Changes TABLE (
    [Id] BIGINT
  );

  DELETE FROM ProtectedBranches
   WHERE RepositoryId = @RepositoryId AND [Name] = @Name;

  UPDATE SyncLog SET 
    [RowVersion] = Default,
    [Delete] = 1
  WHERE ItemType = 'protectedbranch' AND ItemId IN (SELECT Id FROM @Changes);

  SELECT 'repo' as ItemType, @RepositoryId as ItemId
  WHERE EXISTS (SELECT * FROM @Changes);
END

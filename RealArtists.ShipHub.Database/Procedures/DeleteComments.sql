CREATE PROCEDURE [dbo].[DeleteComments]
  @Comments ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DELETE FROM Comments WITH (UPDLOCK SERIALIZABLE)
  WHERE Id IN (SELECT Item FROM @Comments)

  UPDATE SyncLog WITH (UPDLOCK SERIALIZABLE) SET
    [Delete] = 1,
    [RowVersion] = DEFAULT
  -- Crafty change output
  OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
  WHERE ItemType = 'comment'
    AND [Delete] = 0
    AND ItemId IN (SELECT Item FROM @Comments)
END

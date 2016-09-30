CREATE PROCEDURE [dbo].[DeleteComments]
  @Comments ItemListTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DELETE FROM Comments WITH (UPDLOCK SERIALIZABLE)
  WHERE EXISTS (SELECT * FROM @Comments WHERE Item = Id)

  UPDATE RepositoryLog WITH (UPDLOCK SERIALIZABLE) SET
    [Delete] = 1,
    [RowVersion] = DEFAULT
  -- Crafty change output
  OUTPUT NULL as OrganizationId, INSERTED.RepositoryId, NULL as UserId
  WHERE [Type] = 'comment'
    AND EXISTS (SELECT * FROM @Comments WHERE Item = ItemId)
END

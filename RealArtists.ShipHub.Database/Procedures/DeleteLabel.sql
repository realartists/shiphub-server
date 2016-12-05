CREATE PROCEDURE [dbo].[DeleteLabel]
  @LabelId BIGINT
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DELETE FROM IssueLabels WHERE LabelId = @LabelId

  DELETE FROM Labels WHERE Id = @LabelId

  UPDATE SyncLog WITH (UPDLOCK SERIALIZABLE) SET
    [Delete] = 1,
    [RowVersion] = DEFAULT
  -- Crafty change output
  OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
  WHERE ItemType = 'label' AND [Delete] = 0 AND ItemId = @LabelId
END

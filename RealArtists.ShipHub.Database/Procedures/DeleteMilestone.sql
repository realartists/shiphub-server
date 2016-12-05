CREATE PROCEDURE [dbo].[DeleteMilestone]
  @MilestoneId BIGINT
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DELETE FROM Milestones WHERE Id = @MilestoneId

  UPDATE SyncLog WITH (UPDLOCK SERIALIZABLE) SET
    [Delete] = 1,
    [RowVersion] = DEFAULT
  -- Crafty change output
  OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
  WHERE ItemType = 'milestone' AND [Delete] = 0 AND ItemId = @MilestoneId
END

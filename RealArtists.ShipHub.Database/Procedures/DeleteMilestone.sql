CREATE PROCEDURE [dbo].[DeleteMilestone]
  @MilestoneId BIGINT
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DELETE FROM Milestones WHERE Id = @MilestoneId

  UPDATE RepositoryLog WITH (UPDLOCK SERIALIZABLE)
    SET
    [Delete] = 1,
    [RowVersion] = DEFAULT
  OUTPUT NULL as OrganizationId, INSERTED.RepositoryId, NULL as UserId
  WHERE [Type] = 'milestone'
    AND ItemId = @MilestoneId
    AND [Delete] = 0
END

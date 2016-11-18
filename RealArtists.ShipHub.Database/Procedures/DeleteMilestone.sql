CREATE PROCEDURE [dbo].[DeleteMilestone]
  @RepositoryId BIGINT,
  @MilestoneId BIGINT
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DELETE FROM Milestones WHERE Id = @MilestoneId

  IF(@@ROWCOUNT > 0)
  BEGIN
    UPDATE RepositoryLog WITH (UPDLOCK SERIALIZABLE) SET
      [Delete] = 1,
      [RowVersion] = DEFAULT
    WHERE RepositoryId = @RepositoryId
      AND [Type] = 'milestone'
      AND ItemId = @MilestoneId

    -- Return repository if updated
    SELECT NULL as OrganizationId, @RepositoryId as RepositoryId, NULL as UserId
  END
END

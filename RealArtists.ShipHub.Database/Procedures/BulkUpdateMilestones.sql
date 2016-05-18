CREATE PROCEDURE [dbo].[BulkUpdateMilestones]
  @RepositoryId INT,
  @Milestones MilestoneTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  MERGE INTO Milestones as [Target]
  USING (
    SELECT Id, @RepositoryId as RepositoryId, Number, [State], Title, [Description], CreatedAt, UpdatedAt, ClosedAt, DueOn
    FROM @Milestones
  ) as [Source]
  ON ([Target].Id = [Source].Id)
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, RepositoryId, Number, [State], Title, [Description], CreatedAt, UpdatedAt, ClosedAt, DueOn)
    VALUES (Id, @RepositoryId, Number, [State], Title, [Description], CreatedAt, UpdatedAt, ClosedAt, DueOn)
  WHEN MATCHED
    AND [Target].UpdatedAt < [Source].UpdatedAt
    AND EXISTS (
      SELECT [Target].Id, [Target].RepositoryId, [Target].Number, [Target].[State], [Target].Title, [Target].[Description], [Target].CreatedAt, [Target].UpdatedAt, [Target].ClosedAt, [Target].DueOn
      EXCEPT
      SELECT [Source].Id, [Source].RepositoryId, [Source].Number, [Source].[State], [Source].Title, [Source].[Description], [Source].CreatedAt, [Source].UpdatedAt, [Source].ClosedAt, [Source].DueOn
    ) THEN
    UPDATE SET
      Id = [Source].Id,
      RepositoryId = [Source].RepositoryId,
      Number = [Source].Number,
      [State] = [Source].[State], 
      Title = [Source].Title,
      [Description] = [Source].[Description],
      CreatedAt = [Source].CreatedAt,
      UpdatedAt = [Source].UpdatedAt,
      ClosedAt = [Source].ClosedAt,
      DueOn = [Source].DueOn;
END

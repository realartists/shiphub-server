CREATE PROCEDURE [dbo].[GarbageCollectLabels]
  @BatchSize INT = 16384
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  DELETE TOP(@BatchSize)
  FROM Labels
  WHERE Id NOT IN (
    SELECT LabelId FROM IssueLabels
    UNION
    SELECT LabelId FROM RepositoryLabels)

  RETURN @@ROWCOUNT
END

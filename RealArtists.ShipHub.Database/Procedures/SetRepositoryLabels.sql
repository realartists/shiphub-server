CREATE PROCEDURE [dbo].[SetRepositoryLabels]
  @RepositoryId BIGINT,
  @Labels LabelTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  EXEC [dbo].[BulkCreateLabels] @Labels = @Labels

  MERGE INTO RepositoryLabels WITH (SERIALIZABLE) as [Target]
  USING (SELECT L1.Id as LabelId
    FROM Labels as L1
      INNER JOIN @Labels as L2 ON (L1.Color = L2.Color AND L1.Name = L2.Name)
  ) as [Source]
  ON ([Target].LabelId = [Source].LabelId AND [Target].RepositoryId = @RepositoryId)
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (RepositoryId, LabelId)
    VALUES (@RepositoryId, LabelId)
  -- Remove
  WHEN NOT MATCHED BY SOURCE
    AND [Target].RepositoryId = @RepositoryId
    THEN DELETE
  OPTION (RECOMPILE);

  IF(@@ROWCOUNT > 0)
  BEGIN
    -- Update repo log entry
    UPDATE RepositoryLog WITH (SERIALIZABLE)
      SET [RowVersion] = DEFAULT
    WHERE RepositoryId = @RepositoryId
      AND [Type] = 'repository'
      -- AND ItemId = @RepositoryId
  END
END

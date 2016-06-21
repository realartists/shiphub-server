CREATE PROCEDURE [dbo].[BulkUpdateAccounts]
  @Date DATETIMEOFFSET,
  @Accounts AccountTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  DECLARE @Changes TABLE (
    [Id] BIGINT NOT NULL PRIMARY KEY CLUSTERED
  );

  MERGE INTO Accounts WITH (SERIALIZABLE) as [Target]
  USING (
    SELECT Id, [Type], [Login]
    FROM @Accounts
  ) as [Source]
  ON ([Target].Id = [Source].Id)
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, [Type], [Login], [Date])
    VALUES (Id, [Type], [Login], @Date)
  -- Update
  WHEN MATCHED 
    AND [Target].[Date] < @Date
    AND EXISTS (
      SELECT [Target].[Type], [Target].[Login]
      EXCEPT
      SELECT [Source].[Type], [Source].[Login]
    ) THEN
    UPDATE SET
      [Type] = [Source].[Type],
      [Login] = [Source].[Login],
      [Date] = @Date
  OUTPUT INSERTED.Id INTO @Changes (Id)
  OPTION (RECOMPILE);

  -- Other actions manage adding user references to repos.
  -- Our only job here is to mark still valid references as changed.
  UPDATE RepositoryLog WITH (SERIALIZABLE) SET
    [RowVersion] = DEFAULT -- Bump version
  WHERE [Type] = 'account'
    AND [Delete] = 0
    AND ItemId IN (SELECT Id FROM @Changes)
  OPTION (RECOMPILE)
END

CREATE PROCEDURE [dbo].[BulkUpdateAccounts]
  @Date DATETIMEOFFSET,
  @Accounts AccountTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  MERGE INTO Accounts as [Target]
  USING (
    SELECT [Id], [Type], [Login]
    FROM @Accounts
  ) as [Source]
  ON ([Target].Id = [Source].Id)
  WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Id], [Type], [Login], [Date])
    VALUES ([Id], [Type], [Login], @Date)
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
      [Date] = @Date;
  --OUTPUT inserted.[Id], inserted.[Type], inserted.[Login], inserted.[Date];      
END

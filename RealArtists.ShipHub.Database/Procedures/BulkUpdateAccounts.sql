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
    SELECT [Id], [Type], [AvatarUrl], [Login]
    FROM @Accounts
  ) as [Source]
  ON ([Target].Id = [Source].Id)
  WHEN NOT MATCHED BY TARGET THEN
    INSERT ([Id], [Type], [AvatarUrl], [Login], [Date])
    VALUES ([Id], [Type], [AvatarUrl], [Login], @Date)
  WHEN MATCHED 
    AND [Target].[Date] < @Date
    AND EXISTS (
      SELECT [Target].[Type], [Target].[AvatarUrl], [Target].[Login]
      EXCEPT
      SELECT [Source].[Type], [Source].[AvatarUrl], [Source].[Login]
    ) THEN
    UPDATE SET
      [Type] = [Source].[Type],
      [AvatarUrl] = [Source].[AvatarUrl],
      [Login] = [Source].[Login],
      [Date] = @Date;
  --OUTPUT inserted.[Id], inserted.[Type], inserted.[AvatarUrl], inserted.[Login], inserted.[Date];      
END

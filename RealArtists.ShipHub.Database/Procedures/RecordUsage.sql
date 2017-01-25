CREATE PROCEDURE [dbo].[RecordUsage]
  @AccountId BIGINT,
  @Date DATETIMEOFFSET
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  MERGE INTO Usage WITH (SERIALIZABLE) as [Target]
  USING (
    SELECT @AccountId as AccountId, @Date as [Date]
  ) as [Source]
  ON ([Target].AccountId = [Source].AccountId AND [Target].[Date] = [Source].[Date])
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (AccountId, [Date])
    VALUES (AccountId, [Date])
  OPTION (LOOP JOIN, FORCE ORDER);
END

CREATE PROCEDURE [dbo].[RecordUsage]
  @AccountId BIGINT,
  @Date DATETIMEOFFSET
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  INSERT INTO Usage (AccountId, [Date])
  SELECT @AccountId, @Date
  WHERE NOT EXISTS (
    SELECT * FROM Usage
    WHERE AccountId = @AccountId AND [Date] = @Date)
END

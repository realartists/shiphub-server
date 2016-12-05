CREATE PROCEDURE [dbo].[RecordUsage]
  @AccountId BIGINT,
  @Date DATETIMEOFFSET
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  INSERT INTO Usage WITH (SERIALIZABLE) (AccountId, [Date])
  SELECT @AccountId, @Date
  WHERE NOT EXISTS (
    SELECT * FROM Usage WITH (UPDLOCK)
    WHERE AccountId = @AccountId AND [Date] = @Date)
END

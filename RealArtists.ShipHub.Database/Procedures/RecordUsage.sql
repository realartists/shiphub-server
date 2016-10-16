CREATE PROCEDURE [dbo].[RecordUsage]
  @AccountId BIGINT,
  @Date DATETIMEOFFSET
AS
BEGIN
  INSERT INTO Usage WITH (SERIALIZABLE) (AccountId, [Date])
  SELECT @AccountId, @Date
  WHERE NOT EXISTS (SELECT 1 FROM Usage WITH (UPDLOCK) WHERE AccountId = @AccountId AND [Date] = @Date)
END

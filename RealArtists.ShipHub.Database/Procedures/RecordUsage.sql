CREATE PROCEDURE [dbo].[RecordUsage]
  @AccountId BIGINT,
  @Date DATETIMEOFFSET
AS
BEGIN
  MERGE INTO Usage
  USING (SELECT @AccountId as AccountId, @Date as Date) NewUsage
  ON Usage.AccountId = NewUsage.AccountId AND Usage.Date = NewUsage.Date
  WHEN NOT MATCHED THEN
    INSERT (AccountId, Date) VALUES (@AccountId, @Date);
END

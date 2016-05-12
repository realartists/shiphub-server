CREATE PROCEDURE [dbo].[StubAccounts]
  @AccountStubs AccountStubTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  MERGE INTO Accounts as Target
  USING (
    SELECT AccountId, [Type], [Login]
      FROM @AccountStubs) as Source
  ON Target.Id = Source.AccountId
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, [Type], [Login], [Date])
    VALUES (AccountId, [Type], [Login], '1/1/0001 12:00:00 AM +00:00');
END

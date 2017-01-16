CREATE PROCEDURE [dbo].[HookSeen]
  @HookId BIGINT
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  UPDATE Hooks SET
    LastSeen = SYSUTCDATETIME(),
    PingCount = NULL,
    LastPing = NULL
  WHERE Id = @HookId
END

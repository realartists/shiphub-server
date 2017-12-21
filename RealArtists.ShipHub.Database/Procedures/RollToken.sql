CREATE PROCEDURE [dbo].[RollToken]
  @UserId BIGINT,
  @OldToken NVARCHAR(64),
  @NewToken NVARCHAR(64),
  @Version INT
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  UPDATE GitHubTokens SET
    Token = @NewToken,
    [Version] = @Version
  WHERE Token = @OldToken AND UserId = @UserId
END

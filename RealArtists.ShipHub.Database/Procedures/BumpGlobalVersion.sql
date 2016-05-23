CREATE PROCEDURE [dbo].[BumpGlobalVersion]
  @Minimum BIGINT
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON;

  DECLARE @Next BIGINT = NEXT VALUE FOR [dbo].[SyncIdentifier],
          @Largest BIGINT,
          @Buffer BIGINT = 1000,
          @Restart NVARCHAR(MAX)

  --;WITH Versions as (
  --  SELECT MAX([RowVersion]) as [RowVersion] FROM Accounts
  --  UNION SELECT MAX([RowVersion]) FROM Repositories
  --)
  --SELECT @Largest = MAX([RowVersion]) FROM Versions

  IF(@Largest > @Minimum)
  BEGIN
    SET @Minimum = @Largest
  END

  IF(@Minimum > @Next)
  BEGIN
    SET @Restart = CAST(@Minimum + @Buffer as NVARCHAR(MAX))
    EXEC('ALTER SEQUENCE [dbo].[SyncIdentifier] RESTART WITH ' + @Restart)
  END

  RETURN 0
END

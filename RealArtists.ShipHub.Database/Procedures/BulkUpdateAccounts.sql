CREATE PROCEDURE [dbo].[BulkUpdateAccounts]
  @Date DATETIMEOFFSET,
  @Accounts AccountTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- For tracking required updates to sync log
  DECLARE @Changes TABLE (
    [Id]        BIGINT NOT NULL PRIMARY KEY CLUSTERED,
    [BecameOrg] BIT NOT NULL
  )

  DECLARE @OrgChanges TABLE (
    [Id] BIGINT NOT NULL PRIMARY KEY CLUSTERED
  )

  BEGIN TRY
    BEGIN TRANSACTION

    -- Renaming accounts is a seemingly popular thing.
    -- When a new record is found with a conflicting name, rename the old one.
    -- The old one will eventually be updated, or we can force an update.
    -- Don't sync these temporary renames for the time being.
    UPDATE Accounts
      -- This could technically truncate a really long username, but whatever.
      SET [Login] = CAST(N'☠' + a.[Login] as NVARCHAR(255))
    FROM @Accounts as anew
      INNER LOOP JOIN Accounts as a ON (a.[Login] = anew.[Login])
    WHERE a.Id != anew.Id AND a.[Date] < @Date
    OPTION (FORCE ORDER)

    MERGE INTO Accounts WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT Id, [Type], [Login], [Name], Email
      FROM @Accounts
    ) as [Source]
    ON ([Target].Id = [Source].Id)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (Id, [Type], [Login], [Name], Email, [Date])
      VALUES (Id, [Type], [Login], [Name], Email,  @Date)
    -- Update
    WHEN MATCHED 
      AND [Target].[Date] < @Date
      AND EXISTS (
        SELECT [Target].[Type], [Target].[Login], [Target].[Name], [Target].Email
        EXCEPT
        SELECT IIF([Target].[Type] = 'org', [Target].[Type], [Source].[Type]), [Source].[Login], ISNULL([Source].[Name], [Target].[Name]), ISNULL([Source].Email, [Target].Email)
      ) THEN
      UPDATE SET
        -- Once an org, always an org. Even if GitHub lies to us about it.
        [Type] = IIF([Target].[Type] = 'org', [Target].[Type], [Source].[Type]), 
        [Login] = [Source].[Login],
        [Name] = ISNULL([Source].[Name], [Target].[Name]),
        Email = ISNULL([Source].Email, [Target].Email),
        [Date] = @Date
    OUTPUT INSERTED.Id, IIF(ISNULL(DELETED.[Type], INSERTED.[Type]) != INSERTED.[Type], 1, 0) INTO @Changes
    OPTION (LOOP JOIN, FORCE ORDER);

    -- Ensuring organizations reference themselves is handled by
    -- [SetUserOrganizations]

    IF (EXISTS(SELECT * FROM @Changes WHERE BecameOrg = 1))
    BEGIN
      -- Users who transition to orgs no longer have AccountRepositories
      DELETE FROM AccountRepositories
      FROM @Changes as c
        INNER LOOP JOIN AccountRepositories as ar ON (ar.AccountId = c.Id)
      WHERE c.BecameOrg = 1
      OPTION (FORCE ORDER)

      -- Users who transition to orgs also can't log in anymore
      -- This keeps the various sync actors from adding them to pools as well.
      UPDATE Accounts SET
        [Scopes] = DEFAULT,
        [RateLimit] = DEFAULT,
        [RateLimitRemaining] = DEFAULT,
        [RateLimitReset] = DEFAULT
      FROM @Changes as c
        INNER LOOP JOIN Accounts as a ON (a.Id = c.Id)
      WHERE c.BecameOrg = 1
      OPTION (FORCE ORDER)

      DELETE FROM GitHubTokens
      FROM @Changes as c
        INNER LOOP JOIN GitHubTokens as g ON (g.UserId = c.Id)
      WHERE c.BecameOrg = 1
      OPTION (FORCE ORDER)

      -- Users who transition to orgs can't be org members anymore
      DELETE FROM OrganizationAccounts
      OUTPUT DELETED.OrganizationId INTO @OrgChanges
      FROM @Changes as c
        INNER LOOP JOIN OrganizationAccounts as oa ON (oa.UserId = c.Id)
      WHERE c.BecameOrg = 1
      OPTION (FORCE ORDER)

      UPDATE SyncLog SET
        [RowVersion] = DEFAULT -- Bump version
      OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
      FROM @OrgChanges as c
        INNER LOOP JOIN SyncLog as sl ON (sl.OwnerType = 'org' AND sl.OwnerId = c.Id AND sl.ItemType = 'account' and sl.ItemId = c.Id)
      OPTION (FORCE ORDER)

      -- Notify the user-turned-org explcitly so they're booted from sync if connected
      SELECT 'user' as ItemType, Id as ItemId FROM @Changes WHERE BecameOrg = 1

      -- Users who transition to orgs are also no longer assignable,
      -- but the RepositoryActor will pick that up soon enough, if anyone cares.
    END

    -- Other actions manage adding user references to repos.
    -- Our only job here is to mark still valid references as changed.
    -- The LOOP JOIN and FORCE ORDER prevent a scan and merge which deadlocks on PK_SyncLog
    UPDATE SyncLog SET
      [RowVersion] = DEFAULT -- Bump version
    OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
    FROM @Changes as c
      INNER LOOP JOIN SyncLog as sl ON (sl.ItemType = 'account' AND sl.ItemId = c.Id)
    WHERE sl.ItemId != 10137 -- Ghost user (present in most repos. Do not ever mark as updated.)
    OPTION (FORCE ORDER)

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH
END

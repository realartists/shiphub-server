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
    [Id]   BIGINT NOT NULL PRIMARY KEY CLUSTERED,
    [Type] NVARCHAR(4) NOT NULL,
    INDEX IX_Type ([Type])
  )

  DECLARE @OrgChanges TABLE (
    [Id] BIGINT NOT NULL PRIMARY KEY CLUSTERED
  )

  BEGIN TRY
    BEGIN TRANSACTION

    MERGE INTO Accounts WITH (SERIALIZABLE) as [Target]
    USING (
      SELECT Id, [Type], [Login]
      FROM @Accounts
    ) as [Source]
    ON ([Target].Id = [Source].Id)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (Id, [Type], [Login], [Date])
      VALUES (Id, [Type], [Login], @Date)
    -- Update
    WHEN MATCHED 
      AND [Target].[Date] < @Date
      AND EXISTS (
        SELECT [Target].[Type], [Target].[Login]
        EXCEPT
        SELECT [Source].[Type], [Source].[Login]
      ) THEN
      UPDATE SET
        -- Once an org, always an org. Even if GitHub lies to us about it.
        [Type] = IIF([Target].[Type] = 'org', [Target].[Type], [Source].[Type]), 
        [Login] = [Source].[Login],
        [Date] = @Date
    OUTPUT INSERTED.Id, INSERTED.[Type] INTO @Changes
    OPTION (LOOP JOIN, FORCE ORDER);

    -- Ensuring organizations reference themselves is handled by
    -- [SetUserOrganizations]

    IF (EXISTS(SELECT * FROM @Changes WHERE [Type] = 'org'))
    BEGIN
      -- Users who transition to orgs no longer have AccountRepositories
      DELETE FROM AccountRepositories
      FROM @Changes as c
        INNER LOOP JOIN AccountRepositories as ar ON (ar.AccountId = c.Id)
      WHERE c.[Type] = 'org'
      OPTION (FORCE ORDER)

      -- Users who transition to orgs also can't log in anymore
      -- This keeps the various sync actors from adding them to pools as well.
      UPDATE Accounts SET
        [Token] = DEFAULT,
        [Scopes] = DEFAULT,
        [RateLimit] = DEFAULT,
        [RateLimitRemaining] = DEFAULT,
        [RateLimitReset] = DEFAULT
      FROM @Changes as c
        INNER LOOP JOIN Accounts as a ON (a.Id = c.Id)
      WHERE c.[Type] = 'org'
      OPTION (FORCE ORDER)

      -- Users who transition to orgs can't be org members anymore
      DELETE FROM OrganizationAccounts
      OUTPUT DELETED.OrganizationId INTO @OrgChanges
      FROM @Changes as c
        INNER LOOP JOIN OrganizationAccounts as oa ON (oa.UserId = c.Id)
      WHERE c.[Type] = 'org'
      OPTION (FORCE ORDER)

      UPDATE SyncLog SET
        [RowVersion] = DEFAULT -- Bump version
      OUTPUT INSERTED.OwnerType as ItemType, INSERTED.OwnerId as ItemId
      FROM @OrgChanges as c
        INNER LOOP JOIN SyncLog as sl ON (sl.OwnerType = 'org' AND sl.OwnerId = c.Id AND sl.ItemType = 'account' and sl.ItemId = c.Id)
      OPTION (FORCE ORDER)

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

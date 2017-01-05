CREATE PROCEDURE [dbo].[BulkUpdateProjects]
  @RepositoryId BIGINT NULL,
  @OrganizationId BIGINT NULL,  -- OrganizationId and RepositoryId are mutually exclusive. Set one, but not the other.
  @Projects ProjectTableType READONLY
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  -- Validate preconditions
  IF (@OrganizationId IS NOT NULL AND @RepositoryId IS NOT NULL)
    RAISERROR('OrganizationId and RepositoryId are mutually exclusive', 1, 0)

  IF (@OrganizationId IS NULL AND @RepositoryId IS NULL)
    RAISERROR('Either OrganizationId or RepositoryId must be specified', 1, 1)

  DECLARE @OwnerId BIGINT;
  DECLARE @OwnerType NVARCHAR(4);

  SET @OwnerId = COALESCE(@RepositoryId, @OrganizationId);
  SET @OwnerType = CASE WHEN @OrganizationId IS NULL THEN 'repo' ELSE 'org' END;
  
  -- Storage for updates to the log tables
  DECLARE @Changes TABLE (
    [Id] BIGINT NOT NULL PRIMARY KEY CLUSTERED,
    [Action] NVARCHAR(10) NOT NULL
  );

  BEGIN TRY
    BEGIN TRANSACTION

    DELETE FROM Projects
    OUTPUT DELETED.Id, 'DELETE' INTO @Changes
    FROM Projects as p
      LEFT OUTER JOIN @Projects as pp ON (pp.Id = p.Id)
    WHERE p.OrganizationId = @OrganizationId
      AND pp.Id IS NULL
    OPTION (FORCE ORDER)

    DELETE FROM Projects
    OUTPUT DELETED.Id, 'DELETE' INTO @Changes
    FROM Projects as p
      LEFT OUTER JOIN @Projects as pp ON (pp.Id = p.Id)
    WHERE p.RepositoryId = @RepositoryId
      AND pp.Id IS NULL
    OPTION (FORCE ORDER)

    -- Update the Projects table
    MERGE INTO Projects as [Target]
    USING (
      SELECT Id, [Name], Number, Body, CreatedAt, UpdatedAt, CreatorId, @OrganizationId as OrganizationId, @RepositoryId as RepositoryId
      FROM @Projects
    ) as [Source]
    ON ([Target].Id = [Source].Id)
    -- Add
    WHEN NOT MATCHED BY TARGET THEN
      INSERT (Id, [Name], Number, Body, CreatedAt, UpdatedAt, CreatorId, OrganizationId, RepositoryId)
      VALUES (Id, [Name], Number, Body, CreatedAt, UpdatedAt, CreatorId, OrganizationId, RepositoryId)
    -- Update
    WHEN MATCHED AND [Target].UpdatedAt < [Source].UpdatedAt THEN
      UPDATE SET
        [Name] = [Source].[Name],
        Number = [Source].Number,
        Body = [Source].Body,
        CreatedAt = [Source].CreatedAt,
        UpdatedAt = [Source].UpdatedAt,
        CreatorId = [Source].CreatorId,
        OrganizationId = [Source].OrganizationId,
        RepositoryId = [Source].RepositoryId
    OUTPUT INSERTED.Id, $action INTO @Changes
    OPTION(LOOP JOIN, FORCE ORDER);

    -- Update SyncLog with deleted or edited projects
    UPDATE SyncLog SET
      [Delete] = IIF([Action] = 'DELETE', 1, 0),
      [RowVersion] = DEFAULT
    FROM @Changes as c
    INNER JOIN SyncLog ON (OwnerType = @OwnerType AND OwnerId = @OwnerId AND ItemType = 'project' AND ItemId = c.Id)

    -- Update SyncLog with new projects
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT @OwnerType, @OwnerId, 'project', c.Id, 0
    FROM @Changes as c
    WHERE NOT EXISTS (
      SELECT * FROM SyncLog 
      WHERE ItemId = c.Id 
        AND ItemType = 'project' 
        AND OwnerId = @OwnerId
        AND OwnerType = @OwnerType)

    -- Update SyncLog with any newly referenced accounts
    INSERT INTO SyncLog (OwnerType, OwnerId, ItemType, ItemId, [Delete])
    SELECT @OwnerType, @OwnerId, 'account', c.CreatorId, 0
    FROM (
      SELECT DISTINCT(CreatorId) as CreatorId
      FROM @Projects as p
      INNER JOIN @Changes as ch ON (ch.Id = p.Id)
    ) as c
    WHERE NOT EXISTS (
      SELECT * FROM SyncLog
      WHERE OwnerType = @OwnerType AND OwnerId = @OwnerId AND ItemType = 'account' AND ItemId = c.CreatorId)

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF (XACT_STATE() != 0) ROLLBACK TRANSACTION;
    THROW;
  END CATCH

  -- Return sync notifications
  SELECT @OwnerType as ItemType, @OwnerId as ItemId
  WHERE EXISTS (SELECT * FROM @Changes)
END

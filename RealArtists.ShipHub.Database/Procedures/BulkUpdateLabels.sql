﻿CREATE PROCEDURE [dbo].[BulkUpdateLabels]
  @RepositoryId BIGINT,
  @Labels LabelTableType READONLY,
  @Complete BIT = 0
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  DECLARE @Changes BIT = 0

  IF (@Complete = 1)
  BEGIN
    DELETE FROM IssueLabels
    FROM IssueLabels as il WITH (UPDLOCK SERIALIZABLE)
      INNER JOIN Issues as i ON (i.Id = il.IssueId)
    WHERE i.RepositoryId = @RepositoryId
      AND il.LabelId NOT IN (SELECT Id FROM @Labels)

    SET @Changes = CASE WHEN (@@ROWCOUNT > 0) THEN 1 ELSE @Changes END
  END

  MERGE INTO Labels WITH (UPDLOCK SERIALIZABLE) as [Target]
  USING (
    SELECT Id, Name, Color
    FROM @Labels
  ) as [Source]
  ON ([Target].Id = [Source].Id)
  -- Add
  WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, RepositoryId, Name, Color)
    VALUES (Id, @RepositoryId, Name, Color)
  -- Delete
  WHEN NOT MATCHED BY SOURCE AND (@Complete = 1 AND [Target].RepositoryId = @RepositoryId) THEN DELETE
  -- Update
  WHEN MATCHED AND ([Source].Name != [Target].Name OR [Source].Color != [Target].Color) THEN
    UPDATE SET
      Name = [Source].Name,
      Color = [Source].Color;

  SET @Changes = CASE WHEN (@@ROWCOUNT > 0) THEN 1 ELSE @Changes END

  IF(@Changes = 1)
  BEGIN
    UPDATE RepositoryLog WITH (UPDLOCK SERIALIZABLE)
      SET [RowVersion] = DEFAULT
    WHERE RepositoryId = @RepositoryId
      AND [Type] = 'repository'

    -- Return repository if updated
    SELECT NULL as OrganizationId, @RepositoryId as RepositoryId, NULL as UserId
  END
END
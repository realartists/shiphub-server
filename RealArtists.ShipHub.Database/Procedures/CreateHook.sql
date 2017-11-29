CREATE PROCEDURE [dbo].[CreateHook]
  @Secret UNIQUEIDENTIFIER,
  @Events NVARCHAR(500),
  @OrganizationId BIGINT = NULL,
  @RepositoryId BIGINT = NULL
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  INSERT INTO Hooks ([Secret], [Events], OrganizationId, RepositoryId)
  OUTPUT INSERTED.Id, INSERTED.GitHubId, INSERTED.[Secret], INSERTED.[Events], INSERTED.LastSeen,
         INSERTED.RepositoryId, INSERTED.OrganizationId
  VALUES(@Secret, @Events, @OrganizationId, @RepositoryId)
  
  -- Do not signal changes here, since creation on GitHub's side has not yet
  -- been confirmed.
END

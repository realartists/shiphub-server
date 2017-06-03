CREATE PROCEDURE [dbo].[SaveRepositoryMetadata]
  @RepositoryId BIGINT,
  @Size BIGINT = 0,
  @MetadataJson NVARCHAR(MAX) = NULL,
  @AssignableMetadataJson NVARCHAR(MAX) = NULL,
  @IssueMetadataJson NVARCHAR(MAX) = NULL,
  @IssueSince DATETIMEOFFSET = NULL,
  @LabelMetadataJson NVARCHAR(MAX) = NULL,
  @MilestoneMetadataJson NVARCHAR(MAX) = NULL,
  @ProjectMetadataJson NVARCHAR(MAX) = NULL,
  @ContentsRootMetadataJson NVARCHAR(MAX) = NULL,
  @ContentsDotGitHubMetadataJson NVARCHAR(MAX) = NULL,
  @ContentsIssueTemplateMetadataJson NVARCHAR(MAX) = NULL,
  @ContentsPullRequestTemplateMetadataJson NVARCHAR(MAX) = NULL,
  @PullRequestMetadataJson NVARCHAR(MAX) = NULL,
  @PullRequestUpdatedAt DATETIMEOFFSET = NULL,
  @PullRequestSkip INT = NULL
AS
BEGIN
  -- SET NOCOUNT ON added to prevent extra result sets from
  -- interfering with SELECT statements.
  SET NOCOUNT ON

  UPDATE Repositories SET
    Size = @Size,
    MetadataJson = @MetadataJson,
    AssignableMetadataJson = @AssignableMetadataJson,
    IssueMetadataJson = @IssueMetadataJson,
    IssueSince = @IssueSince,
    LabelMetadataJson = @LabelMetadataJson,
    MilestoneMetadataJson = @MilestoneMetadataJson,
    ProjectMetadataJson = @ProjectMetadataJson,
    ContentsRootMetadataJson = @ContentsRootMetadataJson,
    ContentsDotGitHubMetadataJson = @ContentsDotGitHubMetadataJson,
    ContentsIssueTemplateMetadataJson = @ContentsIssueTemplateMetadataJson,
    ContentsPullRequestTemplateMetadataJson = @ContentsPullRequestTemplateMetadataJson,
    PullRequestMetadataJson = @PullRequestMetadataJson,
    PullRequestUpdatedAt = @PullRequestUpdatedAt,
    PullRequestSkip = @PullRequestSkip
  WHERE Id = @RepositoryId
END

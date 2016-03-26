CREATE TABLE [dbo].[Events] (
  [Id]                   INT            NOT NULL,
  [RepositoryId]         INT            NOT NULL,
  [ActorId]              INT            NOT NULL,
  [AssigneeId]           INT            NOT NULL,
  [AssignerId]           INT            NOT NULL,
  [CommitId]             NVARCHAR(40)   NULL,
  [Event]                NVARCHAR(64)   NOT NULL,
  [CreatedAt]            DATETIMEOFFSET NOT NULL,
  -- Label
  [LabelColor]           NVARCHAR(10)   NULL,
  [LabelName]            NVARCHAR(150)  NULL,
  -- Milestone
  [MilestoneId]          INT            NULL,
  [MilestoneNumber]      INT            NULL,
  [MilestoneState]       NVARCHAR(10)   NULL,
  [MilestoneTitle]       NVARCHAR(255)  NULL,
  [MilestoneDescription] NVARCHAR(255)  NULL,
  [MilestoneCreatedAt]   DATETIMEOFFSET NULL,
  [MilestoneUpdatedAt]   DATETIMEOFFSET NULL,
  [MilestoneClosedAt]    DATETIMEOFFSET NULL,
  [MilestoneDueOn]       DATETIMEOFFSET NULL,
  -- Rename
  [RenameFrom]           NVARCHAR(255)  NULL,
  [RenameTo]             NVARCHAR(255)  NULL,
  -- Future
  [ExtensionJson]        NVARCHAR(MAX)  NOT NULL,
  -- MetaData
  [ETag]                 NVARCHAR(64)   NULL,
  [Expires]              DATETIMEOFFSET NULL,
  [LastModified]         DATETIMEOFFSET NULL,
  [LastRefresh]          DATETIMEOFFSET NULL,
  [CacheTokenId]         BIGINT         NULL,
  -- Sync
  [RowVersion]           BIGINT         NULL,
  [RestoreVersion]       BIGINT         NULL,
)

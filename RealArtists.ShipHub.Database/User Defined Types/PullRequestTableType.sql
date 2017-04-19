CREATE TYPE [dbo].[PullRequestTableType] AS TABLE (
  -- Required
  [Id]                  BIGINT           NOT NULL,
  [Number]              INT              NOT NULL PRIMARY KEY CLUSTERED, -- NOT ID (for ordering)
  [IssueId]             BIGINT           NULL, -- Filled by the stored procs
  -- In list and full response
  [CreatedAt]           DATETIMEOFFSET   NOT NULL,
  [UpdatedAt]           DATETIMEOFFSET   NOT NULL,
  [MergeCommitSha]      NVARCHAR(500)    NULL,
  [MergedAt]            DATETIMEOFFSET   NULL,
  [BaseJson]            NVARCHAR(MAX)    NOT NULL,
  [HeadJson]            NVARCHAR(MAX)    NOT NULL,
  -- Only in full response
  [Additions]           INT              NULL,
  [ChangedFiles]        INT              NULL,
  [Commits]             INT              NULL,
  [Deletions]           INT              NULL,
  [MaintainerCanModify] BIT              NULL,
  [Mergeable]           BIT              NULL,
  [MergeableState]      NVARCHAR(25)     NULL,
  [MergedById]          BIGINT           NULL,
  [Rebaseable]          BIT              NULL,
  -- Change tracking (only set for full response)
  [Hash]                UNIQUEIDENTIFIER NULL
)

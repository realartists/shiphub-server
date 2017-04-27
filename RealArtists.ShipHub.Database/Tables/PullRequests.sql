CREATE TABLE [dbo].[PullRequests] (
  -- Required
  [Id]                  BIGINT           NOT NULL,
  [IssueId]             BIGINT           NOT NULL,
  [RepositoryId]        BIGINT           NOT NULL,
  [Number]              INT              NOT NULL,
  -- List Response
  [CreatedAt]           DATETIMEOFFSET   NOT NULL,
  [UpdatedAt]           DATETIMEOFFSET   NOT NULL,
  [MergeCommitSha]      NVARCHAR(500)    NULL,
  [MergedAt]            DATETIMEOFFSET   NULL,
  [BaseJson]            NVARCHAR(MAX)    NOT NULL,
  [HeadJson]            NVARCHAR(MAX)    NOT NULL,
  -- Full Response
  [Additions]           INT              NULL,
  [ChangedFiles]        INT              NULL,
  [Commits]             INT              NULL,
  [Deletions]           INT              NULL,
  [MaintainerCanModify] BIT              NULL,
  [Mergeable]           BIT              NULL,
  [MergeableState]      NVARCHAR(25)     NULL,
  [MergedById]          BIGINT           NULL,
  [Rebaseable]          BIT              NULL,
  -- Change tracking
  [Hash]                UNIQUEIDENTIFIER NULL,
  -- Metadata
  [MetadataJson]        NVARCHAR(MAX)    NULL,
  [CommentMetadataJson] NVARCHAR(MAX)    NULL,
  [StatusMetadataJson]  NVARCHAR(MAX)    NULL,
  CONSTRAINT [PK_PullRequests] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_PullRequests_IssueId_Issues_Id] FOREIGN KEY ([IssueId]) REFERENCES [dbo].[Issues] ([Id]),
  CONSTRAINT [FK_PullRequests_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories] ([Id]),
  CONSTRAINT [FK_PullRequests_MergedById_Accounts_Id] FOREIGN KEY ([MergedById]) REFERENCES [dbo].[Accounts] ([Id])
)
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_PullRequests_IssueId] ON [dbo].[PullRequests]([IssueId])
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_PullRequests_RepositoryId_Number] ON [dbo].[PullRequests]([RepositoryId], [Number])
GO

CREATE NONCLUSTERED INDEX [IX_PullRequests_MergedById] ON [dbo].[PullRequests]([MergedById])
GO

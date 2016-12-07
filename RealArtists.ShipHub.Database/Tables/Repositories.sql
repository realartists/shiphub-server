CREATE TABLE [dbo].[Repositories] (
  [Id]                                BIGINT         NOT NULL,
  [AccountId]                         BIGINT         NOT NULL,
  [Private]                           BIT            NOT NULL,
  [Name]                              NVARCHAR(255)  NOT NULL,
  [FullName]                          NVARCHAR(510)  NOT NULL,
  [Date]                              DATETIMEOFFSET NOT NULL,
  [IssueTemplate]                     NVARCHAR(MAX)  NULL,
  [MetadataJson]                      NVARCHAR(MAX)  NULL,
  [AssignableMetadataJson]            NVARCHAR(MAX)  NULL,
  [CommentMetadataJson]               NVARCHAR(MAX)  NULL,
  [EventMetadataJson]                 NVARCHAR(MAX)  NULL,
  [IssueMetadataJson]                 NVARCHAR(MAX)  NULL,
  [IssueSince]                        DATETIMEOFFSET NULL,
  [LabelMetadataJson]                 NVARCHAR(MAX)  NULL,
  [MilestoneMetadataJson]             NVARCHAR(MAX)  NULL,
  [ProjectMetadataJson]               NVARCHAR(MAX)  NULL,
  [ContentsRootMetadataJson]          NVARCHAR(MAX)  NULL,
  [ContentsDotGitHubMetadataJson]     NVARCHAR(MAX)  NULL,
  [ContentsIssueTemplateMetadataJson] NVARCHAR(MAX)  NULL
  CONSTRAINT [PK_Repositories] PRIMARY KEY CLUSTERED ([Id] ASC),
  CONSTRAINT [FK_Repositories_AccountId_Accounts_Id] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts] ([Id]),
)
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Repositories_FullName] ON [dbo].[Repositories]([FullName])
GO

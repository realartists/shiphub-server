﻿CREATE TYPE [dbo].[RepositoryTableType] AS TABLE (
  [Id]               BIGINT         NOT NULL PRIMARY KEY CLUSTERED,
  [AccountId]        BIGINT         NOT NULL,
  [Private]          BIT            NOT NULL,
  [Name]             NVARCHAR(255)  NOT NULL,
  [FullName]         NVARCHAR(510)  NOT NULL,
  [Size]             BIGINT         NOT NULL,
  [HasIssues]        BIT            NOT NULL,
  [HasProjects]      BIT            NOT NULL,
  [Disabled]         BIT            NULL,
  [Archived]         BIT            NOT NULL,
  [AllowMergeCommit] BIT            NULL,
  [AllowRebaseMerge] BIT            NULL,
  [AllowSquashMerge] BIT            NULL
)

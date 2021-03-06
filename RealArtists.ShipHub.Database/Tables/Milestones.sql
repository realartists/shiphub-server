﻿CREATE TABLE [dbo].[Milestones] (
  [Id]           BIGINT         NOT NULL,
  [RepositoryId] BIGINT         NOT NULL,
  [Number]       INT            NOT NULL,
  [State]        NVARCHAR(10)   NOT NULL,
  [Title]        NVARCHAR(MAX)  NOT NULL,
  [Description]  NVARCHAR(MAX)  NULL,
  [CreatedAt]    DATETIMEOFFSET NOT NULL,
  [UpdatedAt]    DATETIMEOFFSET NOT NULL,
  [ClosedAt]     DATETIMEOFFSET NULL,
  [DueOn]        DATETIMEOFFSET NULL,
  CONSTRAINT [PK_Milestones] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_Milestones_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories]([Id]),
)
GO

CREATE NONCLUSTERED INDEX [IX_Milestones_RepositoryId] ON [dbo].[Milestones]([RepositoryId])
GO

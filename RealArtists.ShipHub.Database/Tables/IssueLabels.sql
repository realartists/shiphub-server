CREATE TABLE [dbo].[IssueLabels] (
  [RepositoryId] INT    NOT NULL,
  [IssueId]      INT    NOT NULL,
  [LabelId]      BIGINT NOT NULL,
  CONSTRAINT [PK_IssueLabels] PRIMARY KEY CLUSTERED ([RepositoryId], [IssueId], [LabelId]),
  CONSTRAINT [FK_IssueLabels_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories]([Id]),
  CONSTRAINT [FK_IssueLabels_IssueId_Issues_Id] FOREIGN KEY ([IssueId]) REFERENCES [dbo].[Issues]([Id]),
  CONSTRAINT [FK_IssueLabels_LabelId_Labels_Id] FOREIGN KEY ([LabelId]) REFERENCES [dbo].[Labels]([Id]),
);
GO

CREATE NONCLUSTERED INDEX [IX_IssueLabels_RepositoryId] ON [dbo].[IssueLabels]([RepositoryId]);
GO

CREATE NONCLUSTERED INDEX [IX_IssueLabels_IssueId] ON [dbo].[IssueLabels]([IssueId]);
GO

CREATE NONCLUSTERED INDEX [IX_IssueLabels_LabelId] ON [dbo].[IssueLabels]([LabelId]);
GO

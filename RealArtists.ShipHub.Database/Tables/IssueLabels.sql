CREATE TABLE [dbo].[IssueLabels] (
  [IssueId]      INT    NOT NULL,
  [LabelId]      BIGINT NOT NULL,
  CONSTRAINT [PK_IssueLabels] PRIMARY KEY CLUSTERED ([IssueId], [LabelId]),
  CONSTRAINT [FK_IssueLabels_IssueId_Issues_Id] FOREIGN KEY ([IssueId]) REFERENCES [dbo].[Issues]([Id]),
  CONSTRAINT [FK_IssueLabels_LabelId_Labels_Id] FOREIGN KEY ([LabelId]) REFERENCES [dbo].[Labels]([Id]),
);
GO

CREATE NONCLUSTERED INDEX [IX_IssueLabels_IssueId] ON [dbo].[IssueLabels]([IssueId]);
GO

CREATE NONCLUSTERED INDEX [IX_IssueLabels_LabelId] ON [dbo].[IssueLabels]([LabelId]);
GO

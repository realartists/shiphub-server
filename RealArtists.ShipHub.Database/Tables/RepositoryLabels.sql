CREATE TABLE [dbo].[RepositoryLabels] (
  [RepositoryId] INT    NOT NULL,
  [LabelId]      BIGINT NOT NULL,
  CONSTRAINT [PK_RepositoryLabels] PRIMARY KEY CLUSTERED ([RepositoryId], [LabelId]),
  CONSTRAINT [FK_RepositoryLabels_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories]([Id]),
  CONSTRAINT [FK_RepositoryLabels_LabelId_Labels_Id] FOREIGN KEY ([LabelId]) REFERENCES [dbo].[Labels]([Id]),
);
GO

CREATE NONCLUSTERED INDEX [IX_RepositoryLabels_RepositoryId] ON [dbo].[RepositoryLabels]([RepositoryId]);
GO

CREATE NONCLUSTERED INDEX [IX_RepositoryLabels_LabelId] ON [dbo].[RepositoryLabels]([LabelId]);
GO

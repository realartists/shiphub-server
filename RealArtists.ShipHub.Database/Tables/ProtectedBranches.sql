CREATE TABLE [dbo].[ProtectedBranches] (
  [Id]           BIGINT        NOT NULL IDENTITY(1,1),
  [RepositoryId] BIGINT        NOT NULL,
  [Name]         NVARCHAR(255) NOT NULL, -- Technically, git allows longer, but GitHub and most filesystems are capped at 255.
  [Protection]   NVARCHAR(MAX) NOT NULL,
  [MetadataJson] NVARCHAR(MAX) NULL,
  CONSTRAINT [PK_ProtectedBranches] PRIMARY KEY CLUSTERED ([Id]),
  CONSTRAINT [FK_ProtectedBranches_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories] ([Id])
)
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_ProtectedBranches_RepositoryId_Name] ON [dbo].[ProtectedBranches]([RepositoryId], [Name])
GO

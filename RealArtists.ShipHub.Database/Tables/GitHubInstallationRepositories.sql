CREATE TABLE [dbo].[GitHubInstallationRepositories] (
  [InstallationId] BIGINT         NOT NULL,
  [RepositoryId]   BIGINT         NOT NULL,
  CONSTRAINT [PK_GitHubInstallationRepositories] PRIMARY KEY CLUSTERED ([InstallationId] ASC, [RepositoryId] ASC),
  CONSTRAINT [FK_GitHubInstallationRepositories_InstallationId_GitHubInstallation_Id] FOREIGN KEY ([InstallationId]) REFERENCES [dbo].[GitHubInstallations] ([Id]),
  CONSTRAINT [FK_GitHubInstallationRepositories_RepositoryId_Repositories_Id] FOREIGN KEY ([RepositoryId]) REFERENCES [dbo].[Repositories] ([Id])
)
GO

CREATE NONCLUSTERED INDEX [IX_GitHubInstallationRepositories_InstallationId] ON [dbo].[GitHubInstallationRepositories]([InstallationId])
GO

CREATE NONCLUSTERED INDEX [IX_GitHubInstallationRepositories_RepositoryId] ON [dbo].[GitHubInstallationRepositories]([RepositoryId])
GO

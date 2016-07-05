CREATE TABLE [dbo].[Repositories] (
  [Id]                   BIGINT         NOT NULL,
  [AccountId]            BIGINT         NOT NULL,
  [Private]              BIT            NOT NULL,
  [Name]                 NVARCHAR(255)  NOT NULL,
  [FullName]             NVARCHAR(510)  NOT NULL,
  [Date]                 DATETIMEOFFSET NOT NULL,
  [AssignableMetaDataId] BIGINT         NULL,
  [LabelMetaDataId]      BIGINT         NULL,
  CONSTRAINT [PK_Repositories] PRIMARY KEY CLUSTERED ([Id] ASC),
  CONSTRAINT [FK_Repositories_AccountId_Accounts_Id] FOREIGN KEY ([AccountId]) REFERENCES [dbo].[Accounts] ([Id]),
  CONSTRAINT [FK_Repositories_AssignableMetaDataId_GitHubMetaData_Id] FOREIGN KEY ([AssignableMetaDataId]) REFERENCES [dbo].[GitHubMetaData]([Id]),
  CONSTRAINT [FK_Repositories_LabeleMetaDataId_GitHubMetaData_Id] FOREIGN KEY ([LabelMetaDataId]) REFERENCES [dbo].[GitHubMetaData]([Id]),
)
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Repositories_FullName] ON [dbo].[Repositories]([FullName])
GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_Repositories_AssignableMetaDataId]
  ON [dbo].[Repositories]([AssignableMetaDataId])
  WHERE ([AssignableMetaDataId] IS NOT NULL)
GO

CREATE UNIQUE NONCLUSTERED INDEX [IX_Repositories_LabelMetaDataId]
  ON [dbo].[Repositories]([LabelMetaDataId])
  WHERE ([LabelMetaDataId] IS NOT NULL)
GO

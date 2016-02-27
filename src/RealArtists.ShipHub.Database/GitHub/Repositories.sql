CREATE TABLE [GitHub].[Repositories] (
  [Id]          INT            NOT NULL,
  [OwnerId]     INT            NOT NULL,
  [Private]     BIT            NOT NULL,
  [HasIssues]   BIT            NOT NULL,
  [Name]        NVARCHAR(100)  NOT NULL,
  [FullName]    NVARCHAR(500)  NOT NULL,
  [Description] NVARCHAR(500)  NOT NULL,
  [CreatedAt]   DATETIMEOFFSET NOT NULL,
  [UpdatedAt]   DATETIMEOFFSET NOT NULL,
  CONSTRAINT [PK_GitHub_Repositories] PRIMARY KEY CLUSTERED ([Id] ASC),
  CONSTRAINT [FKCD_GitHub_Repositories_Accounts] FOREIGN KEY ([OwnerId]) REFERENCES [GitHub].[Accounts] ([Id]) ON DELETE CASCADE
);
GO

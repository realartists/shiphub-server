CREATE TYPE [dbo].[RepositoryStubTableType] AS TABLE (
  [Id]        INT            NOT NULL PRIMARY KEY CLUSTERED,
  [AccountId] INT            NOT NULL,
  [Private]   BIT            NOT NULL,
  [Name]      NVARCHAR(100)  NOT NULL,
  [FullName]  NVARCHAR(255)  NOT NULL
)

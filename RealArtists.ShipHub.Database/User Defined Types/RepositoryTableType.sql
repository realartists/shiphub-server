CREATE TYPE [dbo].[RepositoryTableType] AS TABLE (
  [Id]        BIGINT         NOT NULL PRIMARY KEY CLUSTERED,
  [AccountId] BIGINT         NOT NULL,
  [Private]   BIT            NOT NULL,
  [Name]      NVARCHAR(100)  NOT NULL,
  [FullName]  NVARCHAR(255)  NOT NULL
)

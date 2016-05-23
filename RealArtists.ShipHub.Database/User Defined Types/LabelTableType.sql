CREATE TYPE [dbo].[LabelTableType] AS TABLE (
  [Id]    BIGINT        NOT NULL,
  [Color] NVARCHAR(6)   NOT NULL,
  [Name]  NVARCHAR(150) NOT NULL,
  PRIMARY KEY CLUSTERED ([Id], [Color], [Name])
)

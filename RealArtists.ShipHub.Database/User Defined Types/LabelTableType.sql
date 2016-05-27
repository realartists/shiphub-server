CREATE TYPE [dbo].[LabelTableType] AS TABLE (
  [ItemId] BIGINT        NOT NULL,
  [Color]  NVARCHAR(6)   NOT NULL,
  [Name]   NVARCHAR(150) NOT NULL,
  PRIMARY KEY CLUSTERED ([ItemId], [Color], [Name])
)

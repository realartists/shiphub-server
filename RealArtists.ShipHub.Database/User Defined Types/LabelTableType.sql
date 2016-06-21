CREATE TYPE [dbo].[LabelTableType] AS TABLE (
  [ItemId] BIGINT        NOT NULL,
  [Color]  NVARCHAR(6)   NOT NULL,
  [Name]   NVARCHAR(500) NOT NULL,
  PRIMARY KEY CLUSTERED ([Color], [Name], [ItemId])
)

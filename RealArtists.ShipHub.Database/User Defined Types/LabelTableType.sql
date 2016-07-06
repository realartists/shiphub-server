CREATE TYPE [dbo].[LabelTableType] AS TABLE (
  [ItemId] BIGINT        NOT NULL,
  [Color]  CHAR(6)       NOT NULL,
  [Name]   NVARCHAR(400) NOT NULL,
  PRIMARY KEY CLUSTERED ([Color], [Name], [ItemId])
)

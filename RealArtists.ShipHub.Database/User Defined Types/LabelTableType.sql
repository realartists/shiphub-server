CREATE TYPE [dbo].[LabelTableType] AS TABLE (
  [Color] NVARCHAR(6)   NOT NULL,
  [Name]  NVARCHAR(150) NOT NULL,
  PRIMARY KEY CLUSTERED ([Color], [Name])
)

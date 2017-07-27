CREATE TYPE [dbo].[StringMappingTableType] AS TABLE (
  [Key]   BIGINT        NOT NULL PRIMARY KEY CLUSTERED,
  [Value] NVARCHAR(MAX) NULL
)

CREATE TYPE [dbo].[ProtectedBranchMetadataTableType] AS TABLE (
  [Name]         NVARCHAR(255) NOT NULL PRIMARY KEY CLUSTERED,
  [MetadataJson] NVARCHAR(MAX) NULL
)

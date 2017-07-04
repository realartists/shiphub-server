CREATE TYPE [dbo].[ProtectedBranchMetadataTableType] AS TABLE
(
  Name NVARCHAR(255) NOT NULL,
  MetadataJson NVARCHAR(MAX) NULL
)

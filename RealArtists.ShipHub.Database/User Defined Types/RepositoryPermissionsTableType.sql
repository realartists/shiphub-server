CREATE TYPE [dbo].[RepositoryPermissionsTableType] AS TABLE (
  [RepositoryId] BIGINT NOT NULL PRIMARY KEY CLUSTERED,
  [Admin]        BIT    NOT NULL,
  [Push]         BIT    NOT NULL,
  [Pull]         BIT    NOT NULL
)

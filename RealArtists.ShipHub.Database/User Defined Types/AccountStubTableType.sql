CREATE TYPE [dbo].[AccountStubTableType] AS TABLE (
  [Id]    INT           NOT NULL PRIMARY KEY CLUSTERED,
  [Type]  NVARCHAR(4)   NOT NULL,
  [Login] NVARCHAR(255) NOT NULL
)

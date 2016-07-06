CREATE TABLE [dbo].[Labels] (
  [Id]    BIGINT        IDENTITY(1,1) NOT NULL,
  [Color] CHAR(6)       NOT NULL,
  [Name]  NVARCHAR(400) NOT NULL,
  CONSTRAINT [PK_Labels] PRIMARY KEY CLUSTERED ([Id] ASC),
)
GO

CREATE UNIQUE NONCLUSTERED INDEX [UIX_Labels_Color_Name] ON [dbo].[Labels]([Color], [Name]);
GO

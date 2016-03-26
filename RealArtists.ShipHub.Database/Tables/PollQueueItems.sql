CREATE TABLE [dbo].[PollQueueItems] (
  [Id]           BIGINT NOT NULL,
  [ResourceType] NVARCHAR(128) NOT NULL,
  [ResourceName] NVARCHAR(128) NOT NULL,
  [NotBefore]    DATETIMEOFFSET NOT NULL,
  [Impersonate]  INT NOT NULL,
  [Spider]       BIT NOT NULL,
  [Extra]        NVARCHAR(MAX) NULL,
)

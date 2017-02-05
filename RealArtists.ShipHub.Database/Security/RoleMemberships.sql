ALTER ROLE [db_owner] ADD MEMBER [ShipUser]
GO

ALTER ROLE [db_datareader] ADD MEMBER [ReadOnly]
GO

ALTER ROLE [db_denydatawriter] ADD MEMBER [ReadOnly]
GO

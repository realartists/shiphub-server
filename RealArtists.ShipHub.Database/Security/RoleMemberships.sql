ALTER ROLE [db_owner] ADD MEMBER [ShipUser]
GO

-- ReadOnly User
ALTER ROLE [db_datareader] ADD MEMBER [ReadOnly]
GO

ALTER ROLE [db_denydatawriter] ADD MEMBER [ReadOnly]
GO

-- StateOnly User
ALTER ROLE [db_denydatareader] ADD MEMBER [StateOnly]
GO

ALTER ROLE [db_denydatawriter] ADD MEMBER [StateOnly]
GO

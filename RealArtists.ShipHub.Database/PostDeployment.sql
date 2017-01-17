/*
Post-Deployment Script Template              
--------------------------------------------------------------------------------------
 This file contains SQL statements that will be appended to the build script.    
 Use SQLCMD syntax to include a file in the post-deployment script.      
 Example:      :r .\myfile.sql                
 Use SQLCMD syntax to reference a variable in the post-deployment script.    
 Example:      :setvar TableName MyTable              
               SELECT * FROM [$(TableName)]          
--------------------------------------------------------------------------------------
*/

DECLARE @Version SQL_VARIANT = SERVERPROPERTY('Edition')
DECLARE @SQL NVARCHAR(MAX) = N'
DECLARE @Create BIT = 0
SELECT @Create = CASE WHEN NOT EXISTS (SELECT * FROM sys.server_event_sessions WHERE [name] = ''ShipHub_Deadlocks'') THEN 1 ELSE 0 END

IF (@Create = 1)
BEGIN
  CREATE EVENT SESSION [ShipHub_Deadlocks] ON SERVER
  ADD EVENT sqlserver.database_xml_deadlock_report(
	  ACTION(sqlserver.plan_handle,sqlserver.sql_text,sqlserver.tsql_stack))
  ADD TARGET package0.ring_buffer(SET max_events_limit=(2500))
  WITH (MAX_MEMORY=4096 KB,EVENT_RETENTION_MODE=ALLOW_SINGLE_EVENT_LOSS,MAX_DISPATCH_LATENCY=30 SECONDS,MAX_EVENT_SIZE=0 KB,MEMORY_PARTITION_MODE=NONE,TRACK_CAUSALITY=OFF,STARTUP_STATE=ON)
END
'

IF (@Version = 'SQL Azure')
BEGIN
	SET @SQL = REPLACE(@SQL, 'sys.server_event_sessions', 'sys.database_event_sessions')
	SET @SQL = REPLACE(@SQL, 'ON SERVER', 'ON DATABASE')
END

EXECUTE(@SQL)

SET @SQL = N'
ALTER EVENT SESSION ShipHub_Deadlocks ON SERVER STATE = START; 
'

IF (@Version = 'SQL Azure')
BEGIN
	SET @SQL = REPLACE(@SQL, 'ON SERVER', 'ON DATABASE')
END

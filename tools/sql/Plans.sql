select object_name(objectid) as [Name], *
from sys.dm_exec_cached_plans as cp
	cross apply sys.dm_exec_query_plan(cp.plan_handle)
where object_name(objectid) IS NOT NULL
--where objectid = object_id('[dbo].[BulkUpdateMilestones]')
order by object_name(objectid) ASC
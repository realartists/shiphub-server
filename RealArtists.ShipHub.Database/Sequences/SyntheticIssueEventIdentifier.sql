﻿CREATE SEQUENCE [dbo].[SyntheticIssueEventIdentifier]
    AS BIGINT
    START WITH 9223372036854775807 -- 1<<63
    INCREMENT BY -1
    MINVALUE 2147483647 -- 1<<31
    NO MAXVALUE
    NO CYCLE
    CACHE 10

GO

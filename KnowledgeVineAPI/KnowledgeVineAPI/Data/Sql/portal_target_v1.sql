DO $knowledgevine_target$
BEGIN
    IF current_database() NOT IN ('knowledgevine_local', 'knowledgevine', 'knowledgevine_live') THEN
        RAISE EXCEPTION 'KnowledgeVine database target required' USING ERRCODE = '28000';
    END IF;
END $knowledgevine_target$;

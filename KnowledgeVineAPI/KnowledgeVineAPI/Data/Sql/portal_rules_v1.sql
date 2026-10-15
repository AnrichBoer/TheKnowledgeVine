-- Versioned migration resource. Only run inside a KnowledgeVine database.
DO $$
BEGIN
    IF current_database() NOT IN ('knowledgevine_local', 'knowledgevine', 'knowledgevine_live') THEN
        RAISE EXCEPTION 'KnowledgeVine database target required' USING ERRCODE = '28000';
    END IF;
END $$;

CREATE FUNCTION public.portal_user_has_role(p_user_id uuid, p_role text)
RETURNS boolean LANGUAGE sql STABLE SECURITY INVOKER SET search_path = pg_catalog, public AS $$
    SELECT EXISTS (
        SELECT 1 FROM public.users u
        JOIN public.user_roles ur ON ur.user_id = u.id
        JOIN public.roles r ON r.id = ur.role_id
        WHERE u.id = p_user_id AND u.is_active AND r.normalized_name = upper(p_role)
    );
$$;

CREATE FUNCTION public.portal_user_is_ready(p_user_id uuid)
RETURNS boolean LANGUAGE sql STABLE SECURITY INVOKER SET search_path = pg_catalog, public AS $$
    SELECT EXISTS (SELECT 1 FROM public.users u WHERE u.id = p_user_id
        AND u.is_active AND u.email_confirmed AND coalesce(u.password_hash, '') <> '');
$$;

CREATE FUNCTION public.portal_teacher_is_ready(p_teacher_user_id uuid)
RETURNS boolean LANGUAGE sql STABLE SECURITY INVOKER SET search_path = pg_catalog, public AS $$
    SELECT public.portal_user_is_ready(p_teacher_user_id)
       AND public.portal_user_has_role(p_teacher_user_id, 'Teacher');
$$;

CREATE FUNCTION public.portal_stamp_updated_at()
RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER SET search_path = pg_catalog, public AS $$
BEGIN
    NEW.created_at := OLD.created_at;
    NEW.updated_at := clock_timestamp();
    RETURN NEW;
END;
$$;
CREATE TRIGGER users_stamp_updated_at BEFORE UPDATE ON public.users
FOR EACH ROW EXECUTE FUNCTION public.portal_stamp_updated_at();
CREATE TRIGGER students_stamp_updated_at BEFORE UPDATE ON public.students
FOR EACH ROW EXECUTE FUNCTION public.portal_stamp_updated_at();

CREATE FUNCTION public.portal_check_teacher_assignment()
RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER SET search_path = pg_catalog, public AS $$
BEGIN
    IF TG_OP = 'UPDATE' AND NEW.teacher_user_id IS NOT DISTINCT FROM OLD.teacher_user_id THEN
        RETURN NEW;
    END IF;
    IF NEW.teacher_user_id IS NOT NULL AND NOT public.portal_teacher_is_ready(NEW.teacher_user_id) THEN
        RAISE EXCEPTION 'Assigned teacher must be active and have completed password setup'
            USING ERRCODE = '23514', CONSTRAINT = 'ck_students_teacher_ready';
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER students_check_teacher BEFORE INSERT OR UPDATE OF teacher_user_id ON public.students
FOR EACH ROW EXECUTE FUNCTION public.portal_check_teacher_assignment();

CREATE FUNCTION public.portal_check_parent_link()
RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER SET search_path = pg_catalog, public AS $$
BEGIN
    IF TG_OP = 'UPDATE' THEN
        RAISE EXCEPTION 'Replace parent links explicitly; original grants are immutable' USING ERRCODE = '23514';
    END IF;
    IF NOT public.portal_user_has_role(NEW.parent_user_id, 'Parent') THEN
        RAISE EXCEPTION 'An active parent account is required' USING ERRCODE = '23514';
    END IF;
    IF NOT public.portal_user_has_role(NEW.linked_by_user_id, 'Admin')
       OR NOT public.portal_user_is_ready(NEW.linked_by_user_id) THEN
        RAISE EXCEPTION 'Parent links require a configured admin account' USING ERRCODE = '42501';
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER parent_students_check_link BEFORE INSERT OR UPDATE ON public.parent_students
FOR EACH ROW EXECUTE FUNCTION public.portal_check_parent_link();

CREATE FUNCTION public.portal_check_daily_update()
RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER SET search_path = pg_catalog, public AS $$
DECLARE
    v_actor uuid;
    v_student public.students%ROWTYPE;
    v_now timestamptz := clock_timestamp();
BEGIN
    IF TG_OP = 'INSERT' THEN
        v_actor := NEW.created_by_user_id;
        NEW.updated_by_user_id := NEW.created_by_user_id;
        NEW.created_at := v_now;
        NEW.version := 1;
    ELSE
        v_actor := NEW.updated_by_user_id;
        IF NEW.student_id IS DISTINCT FROM OLD.student_id
           OR NEW.update_date IS DISTINCT FROM OLD.update_date
           OR NEW.created_by_user_id IS DISTINCT FROM OLD.created_by_user_id THEN
            RAISE EXCEPTION 'A daily note cannot be moved to another child, date or original author' USING ERRCODE = '23514';
        END IF;
        IF OLD.status = 'shared' AND NEW.status <> 'shared' THEN
            RAISE EXCEPTION 'Shared notes remain shared when corrected' USING ERRCODE = '23514';
        END IF;
        NEW.created_at := OLD.created_at;
        NEW.version := OLD.version + 1;
    END IF;

    SELECT * INTO v_student FROM public.students WHERE id = NEW.student_id FOR SHARE;
    IF NOT FOUND OR NOT v_student.is_active OR v_student.teacher_user_id IS DISTINCT FROM v_actor
       OR NOT public.portal_teacher_is_ready(v_actor) THEN
        RAISE EXCEPTION 'Only the active assigned teacher can write this note' USING ERRCODE = '42501';
    END IF;
    NEW.updated_at := v_now;
    IF NEW.status = 'shared' THEN
        IF TG_OP = 'UPDATE' AND OLD.status = 'shared' THEN
            NEW.shared_at := OLD.shared_at;
        ELSE
            NEW.shared_at := v_now;
        END IF;
    ELSE
        NEW.shared_at := NULL;
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER daily_updates_check_write BEFORE INSERT OR UPDATE ON public.daily_updates
FOR EACH ROW EXECUTE FUNCTION public.portal_check_daily_update();

CREATE FUNCTION public.portal_assign_teacher(p_admin_user_id uuid, p_student_id uuid, p_teacher_user_id uuid)
RETURNS void LANGUAGE plpgsql SECURITY INVOKER SET search_path = pg_catalog, public AS $$
BEGIN
    IF NOT public.portal_user_has_role(p_admin_user_id, 'Admin') OR NOT public.portal_user_is_ready(p_admin_user_id) THEN
        RAISE EXCEPTION 'Admin access required' USING ERRCODE = '42501';
    END IF;
    UPDATE public.students SET teacher_user_id = p_teacher_user_id WHERE id = p_student_id AND is_active;
    IF NOT FOUND THEN RAISE EXCEPTION 'Active student not found' USING ERRCODE = 'P0002'; END IF;
END;
$$;

CREATE FUNCTION public.portal_link_parent(p_admin_user_id uuid, p_student_id uuid, p_parent_user_id uuid)
RETURNS void LANGUAGE plpgsql SECURITY INVOKER SET search_path = pg_catalog, public AS $$
BEGIN
    IF NOT public.portal_user_has_role(p_admin_user_id, 'Admin') OR NOT public.portal_user_is_ready(p_admin_user_id) THEN
        RAISE EXCEPTION 'Admin access required' USING ERRCODE = '42501';
    END IF;
    INSERT INTO public.parent_students(parent_user_id, student_id, linked_by_user_id)
    VALUES(p_parent_user_id, p_student_id, p_admin_user_id)
    ON CONFLICT(parent_user_id, student_id) DO NOTHING;
END;
$$;

CREATE FUNCTION public.portal_unlink_parent(p_admin_user_id uuid, p_student_id uuid, p_parent_user_id uuid)
RETURNS void LANGUAGE plpgsql SECURITY INVOKER SET search_path = pg_catalog, public AS $$
BEGIN
    IF NOT public.portal_user_has_role(p_admin_user_id, 'Admin') OR NOT public.portal_user_is_ready(p_admin_user_id) THEN
        RAISE EXCEPTION 'Admin access required' USING ERRCODE = '42501';
    END IF;
    DELETE FROM public.parent_students WHERE parent_user_id = p_parent_user_id AND student_id = p_student_id;
END;
$$;

CREATE FUNCTION public.portal_save_daily_update(
    p_teacher_user_id uuid, p_student_id uuid, p_update_date date, p_note_text text,
    p_share boolean DEFAULT false, p_expected_version integer DEFAULT NULL)
RETURNS public.daily_updates LANGUAGE plpgsql SECURITY INVOKER SET search_path = pg_catalog, public AS $$
DECLARE
    v_student public.students%ROWTYPE;
    v_note public.daily_updates%ROWTYPE;
    v_result public.daily_updates%ROWTYPE;
BEGIN
    IF p_update_date IS NULL OR p_note_text IS NULL OR p_share IS NULL THEN
        RAISE EXCEPTION 'A school date, note text and share choice are required' USING ERRCODE = '22023';
    END IF;
    SELECT * INTO v_student FROM public.students WHERE id = p_student_id FOR UPDATE;
    IF NOT FOUND OR NOT v_student.is_active OR v_student.teacher_user_id IS DISTINCT FROM p_teacher_user_id
       OR NOT public.portal_teacher_is_ready(p_teacher_user_id) THEN
        RAISE EXCEPTION 'Only the active assigned teacher can write this note' USING ERRCODE = '42501';
    END IF;
    SELECT * INTO v_note FROM public.daily_updates
    WHERE student_id = p_student_id AND update_date = p_update_date FOR UPDATE;
    IF FOUND THEN
        IF p_expected_version IS NULL OR p_expected_version <> v_note.version THEN
            RAISE EXCEPTION 'The note changed; reload before saving' USING ERRCODE = '40001';
        END IF;
        UPDATE public.daily_updates SET note_text = p_note_text,
            status = CASE WHEN p_share OR v_note.status = 'shared' THEN 'shared' ELSE 'draft' END,
            updated_by_user_id = p_teacher_user_id
        WHERE id = v_note.id RETURNING * INTO v_result;
    ELSE
        IF p_expected_version IS NOT NULL THEN
            RAISE EXCEPTION 'The note no longer exists; reload before saving' USING ERRCODE = '40001';
        END IF;
        INSERT INTO public.daily_updates(id, student_id, update_date, note_text, status, created_by_user_id, updated_by_user_id)
        VALUES(gen_random_uuid(), p_student_id, p_update_date, p_note_text,
            CASE WHEN p_share THEN 'shared' ELSE 'draft' END, p_teacher_user_id, p_teacher_user_id)
        RETURNING * INTO v_result;
    END IF;
    RETURN v_result;
END;
$$;

CREATE FUNCTION public.portal_parent_updates(
    p_parent_user_id uuid, p_student_id uuid DEFAULT NULL, p_limit integer DEFAULT 50, p_offset integer DEFAULT 0)
RETURNS TABLE(update_id uuid, student_id uuid, student_first_name text, student_last_name text,
    update_date date, note_text text, teacher_name text, shared_at timestamptz, updated_at timestamptz, edited_by_name text)
LANGUAGE plpgsql STABLE SECURITY INVOKER SET search_path = pg_catalog, public AS $$
BEGIN
    IF NOT public.portal_user_has_role(p_parent_user_id, 'Parent') OR NOT public.portal_user_is_ready(p_parent_user_id) THEN
        RAISE EXCEPTION 'Configured parent account required' USING ERRCODE = '42501';
    END IF;
    IF p_limit IS NULL OR p_limit NOT BETWEEN 1 AND 100 OR p_offset IS NULL OR p_offset < 0 THEN
        RAISE EXCEPTION 'Invalid feed pagination' USING ERRCODE = '22023';
    END IF;
    IF p_student_id IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM public.parent_students ps WHERE ps.parent_user_id = p_parent_user_id AND ps.student_id = p_student_id
    ) THEN RAISE EXCEPTION 'Student access denied' USING ERRCODE = '42501'; END IF;
    RETURN QUERY
    SELECT d.id, s.id, s.first_name::text, s.last_name::text, d.update_date, d.note_text,
        (u.first_name || ' ' || u.last_name)::text, d.shared_at, d.updated_at,
        CASE WHEN d.updated_at > d.shared_at THEN (editor.first_name || ' ' || editor.last_name)::text ELSE NULL END
    FROM public.daily_updates d
    JOIN public.students s ON s.id = d.student_id
    JOIN public.parent_students ps ON ps.student_id = s.id AND ps.parent_user_id = p_parent_user_id
    JOIN public.users u ON u.id = d.created_by_user_id
    JOIN public.users editor ON editor.id = d.updated_by_user_id
    WHERE d.status = 'shared' AND (p_student_id IS NULL OR s.id = p_student_id)
    ORDER BY d.update_date DESC, d.shared_at DESC, d.id LIMIT p_limit OFFSET p_offset;
END;
$$;

CREATE FUNCTION public.portal_teacher_students(
    p_teacher_user_id uuid, p_update_date date DEFAULT (timezone('Africa/Johannesburg', CURRENT_TIMESTAMP))::date)
RETURNS TABLE(student_id uuid, first_name text, last_name text, update_id uuid, status text, version integer)
LANGUAGE plpgsql STABLE SECURITY INVOKER SET search_path = pg_catalog, public AS $$
BEGIN
    IF NOT public.portal_teacher_is_ready(p_teacher_user_id) THEN
        RAISE EXCEPTION 'Configured teacher account required' USING ERRCODE = '42501';
    END IF;
    RETURN QUERY
    SELECT s.id, s.first_name::text, s.last_name::text, d.id,
        coalesce(d.status, 'not_started')::text, d.version
    FROM public.students s LEFT JOIN public.daily_updates d ON d.student_id = s.id AND d.update_date = p_update_date
    WHERE s.teacher_user_id = p_teacher_user_id AND s.is_active
    ORDER BY s.last_name, s.first_name, s.id;
END;
$$;

-- Functions use the caller's privileges; grant runtime execution explicitly during provisioning.
REVOKE EXECUTE ON ALL FUNCTIONS IN SCHEMA public FROM PUBLIC;

-- Retain history; parent access follows the current South African calendar year.
CREATE OR REPLACE FUNCTION public.portal_parent_updates(
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
        SELECT 1 FROM public.parent_students ps JOIN public.students s ON s.id = ps.student_id AND s.is_active WHERE ps.parent_user_id = p_parent_user_id AND ps.student_id = p_student_id
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
    WHERE s.is_active AND d.status = 'shared'
        AND d.update_date >= make_date(extract(year FROM timezone('Africa/Johannesburg', CURRENT_TIMESTAMP))::integer, 1, 1)
        AND d.update_date < make_date(extract(year FROM timezone('Africa/Johannesburg', CURRENT_TIMESTAMP))::integer + 1, 1, 1)
        AND (p_student_id IS NULL OR s.id = p_student_id)
    ORDER BY d.update_date DESC, d.shared_at DESC, d.id LIMIT p_limit OFFSET p_offset;
END;
$$;

DO $$
DECLARE
    fk record;
BEGIN
    FOR fk IN
        SELECT tn.nspname AS schema_name, t.relname AS table_name, c.conname AS constraint_name
        FROM pg_constraint c
        JOIN pg_class t ON t.oid = c.conrelid
        JOIN pg_namespace tn ON tn.oid = t.relnamespace
        JOIN pg_class r ON r.oid = c.confrelid
        JOIN pg_namespace rn ON rn.oid = r.relnamespace
        WHERE c.contype = 'f' AND tn.nspname <> rn.nspname
          AND tn.nspname IN ('identity', 'catalog', 'sellers', 'ordering', 'finance', 'engagement', 'marketing', 'platform')
    LOOP
        EXECUTE format('ALTER TABLE %I.%I DROP CONSTRAINT %I', fk.schema_name, fk.table_name, fk.constraint_name);
    END LOOP;
END
$$;

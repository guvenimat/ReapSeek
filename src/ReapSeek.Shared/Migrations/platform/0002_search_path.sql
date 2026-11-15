DO $$
BEGIN
    EXECUTE format('ALTER DATABASE %I SET search_path = platform, identity, catalog, sellers, ordering, finance, engagement, marketing, public', current_database());
END
$$;

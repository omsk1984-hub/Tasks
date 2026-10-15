-- Инициализация БД Tasks.
-- Выполняется автоматически при первом старте контейнера Tasks_postgres
-- (скрипты из /docker-entrypoint-initdb.d).
--
-- ВАЖНО: POSTGRES_DB/POSTGRES_USER/POSTGRES_PASSWORD создаются образом postgres
-- до запуска этого скрипта, поэтому здесь дополнительно создаём расширения.

-- Расширение для генерации UUID (на случай серверной генерации, сейчас Id делает приложение).
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- Расширение для триграммного поиска (задел на фильтрацию по Title, не обязательно по ТЗ).
CREATE EXTENSION IF NOT EXISTS "pg_trgm";

-- Схема для приложения (public используется EF Core по умолчанию; оставляем как есть).
-- Таблицу tasks создаёт EF Core через миграции (db/apply-migrations.ps1).

DO $$
BEGIN
    RAISE NOTICE 'Tasks DB initialized: pgcrypto, pg_trgm ready.';
END
$$;

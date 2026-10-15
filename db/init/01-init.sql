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

-- Право REPLICATION текущему пользователю (tasks_user): нужно CDC-воркеру
-- (Tasks.CDC) для чтения WAL через логическую репликацию (pgoutput).
-- Пользователь создаётся образом postgres до запуска этого скрипта.
DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'tasks_user') THEN
        EXECUTE 'ALTER ROLE tasks_user WITH REPLICATION';
        RAISE NOTICE 'Роль tasks_user получила право REPLICATION.';
    END IF;
END
$$;

DO $$
BEGIN
    RAISE NOTICE 'Tasks DB initialized: pgcrypto, pg_trgm ready.';
END
$$;

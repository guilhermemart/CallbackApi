-- Script de inicialização do PostgreSQL
-- Executado automaticamente na primeira inicialização do banco
-- Cria postgresql.auto.conf que tem prioridade sobre postgresql.conf

-- Configurar max_connections
ALTER SYSTEM SET max_connections = 500;

-- Configurar shared_buffers (25% de max_connections recomendado)
ALTER SYSTEM SET shared_buffers = '256MB';

-- Outras otimizações recomendadas
ALTER SYSTEM SET effective_cache_size = '1GB';
ALTER SYSTEM SET maintenance_work_mem = '64MB';
ALTER SYSTEM SET checkpoint_completion_target = 0.9;
ALTER SYSTEM SET wal_buffers = '16MB';
ALTER SYSTEM SET default_statistics_target = 100;
ALTER SYSTEM SET random_page_cost = 1.1;
ALTER SYSTEM SET effective_io_concurrency = 200;
ALTER SYSTEM SET work_mem = '4MB';
ALTER SYSTEM SET min_wal_size = '1GB';
ALTER SYSTEM SET max_wal_size = '4GB';


-- Script executado automaticamente na primeira inicialização do container PostgreSQL.
-- Responsabilidade: garantir extensões e configurações de banco necessárias.

-- Habilita a extensão para geração de UUIDs nativos no PostgreSQL.
-- Usaremos uuid_generate_v4() como default para PKs das entidades.
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";


-- Habilita a extensão para funções criptográficas (hashing, etc.)
-- Útil para auditoria e segurança em operações financeiras.
CREATE EXTENSION IF NOT EXISTS "pgcrypto";
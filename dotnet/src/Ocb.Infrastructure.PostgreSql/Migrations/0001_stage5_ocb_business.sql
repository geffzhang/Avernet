-- Stage-5 migration: Skills, Caller Identity
-- Schema: ocb_business
--
-- Prerequisites: PostgreSQL server 14+ with ocb_business schema created
-- and connection user with USAGE + CREATE privileges.
-- Apply with: psql -U <user> -d <db> -f 0001_stage5_ocb_business.sql

-- Skill publications table
CREATE TABLE IF NOT EXISTS ocb_business.skill_publications (
    tenant_id              VARCHAR(64)  NOT NULL,
    bot_id                 VARCHAR(128) NOT NULL,
    skill_id               VARCHAR(256) NOT NULL,
    source_locator         TEXT         NOT NULL,
    immutable_version      VARCHAR(256) NOT NULL,
    scheme                 INTEGER      NOT NULL DEFAULT 0,
    package_sha256         CHAR(64)     NOT NULL,
    publication_state      VARCHAR(32)  NOT NULL DEFAULT 'draft',
    manifest_contract_version VARCHAR(64) NOT NULL DEFAULT 'skills-pool-p3-v1',
    created_at             TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at             TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT pk_skill_publications PRIMARY KEY (tenant_id, bot_id, skill_id)
);

CREATE INDEX IF NOT EXISTS ix_sp_tenant_bot
    ON ocb_business.skill_publications (tenant_id, bot_id);

CREATE INDEX IF NOT EXISTS ix_sp_publication_state
    ON ocb_business.skill_publications (tenant_id, publication_state);

-- Caller identity bindings table
CREATE TABLE IF NOT EXISTS ocb_business.caller_identities (
    tenant_id   VARCHAR(64)  NOT NULL,
    bot_id      VARCHAR(128) NOT NULL,
    subject_id  VARCHAR(256) NOT NULL,
    roles       TEXT[]       NOT NULL DEFAULT '{}',
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT now(),

    CONSTRAINT pk_caller_identities PRIMARY KEY (tenant_id, bot_id, subject_id)
);

CREATE INDEX IF NOT EXISTS ix_ci_tenant_bot
    ON ocb_business.caller_identities (tenant_id, bot_id);

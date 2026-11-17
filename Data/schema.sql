-- =====================================================================
-- Sarajevo Communal Issue Reporting: database schema (PostgreSQL)
-- Mobile app (citizens) + web dashboard (operators, admins)
-- =====================================================================

-- ---------- ENUM TYPES ----------
CREATE TYPE user_role     AS ENUM ('citizen', 'operator', 'admin');
CREATE TYPE user_status   AS ENUM ('active', 'suspended');
CREATE TYPE report_status AS ENUM ('submitted', 'in_review', 'assigned', 'in_progress', 'resolved', 'rejected');
CREATE TYPE priority_level AS ENUM ('low', 'normal', 'high');
CREATE TYPE report_source AS ENUM ('mobile', 'web');
CREATE TYPE photo_kind    AS ENUM ('report', 'resolution');   -- citizen photo vs. staff "after" photo
CREATE TYPE device_platform AS ENUM ('ios', 'android', 'web');

-- ---------- DEPARTMENTS ----------
CREATE TABLE departments (
    id          SERIAL PRIMARY KEY,
    name_bs     VARCHAR(150) NOT NULL,
    name_en     VARCHAR(150) NOT NULL,
    contact_email VARCHAR(255),
    is_active   BOOLEAN NOT NULL DEFAULT TRUE
);

-- ---------- USERS ----------
CREATE TABLE users (
    id              SERIAL PRIMARY KEY,
    email           VARCHAR(255) NOT NULL UNIQUE,
    password_hash   VARCHAR(255) NOT NULL,
    first_name      VARCHAR(100) NOT NULL,
    last_name       VARCHAR(100) NOT NULL,
    display_name    VARCHAR(100),
    phone           VARCHAR(30),
    avatar_url      TEXT,
    role            user_role   NOT NULL DEFAULT 'citizen',
    status          user_status NOT NULL DEFAULT 'active',
    department_id   INT REFERENCES departments(id),          -- only for operators
    language        CHAR(2) NOT NULL DEFAULT 'bs' CHECK (language IN ('bs', 'en')),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at      TIMESTAMPTZ,                              -- soft delete for "Delete Account"
    CHECK (role <> 'operator' OR department_id IS NOT NULL)
);

-- ---------- CATEGORIES ----------
-- Flexible: admins can add, rename and disable categories.
-- Each category routes to exactly one department.
CREATE TABLE categories (
    id               SERIAL PRIMARY KEY,
    name_bs          VARCHAR(100) NOT NULL,
    name_en          VARCHAR(100) NOT NULL,
    icon             VARCHAR(50),
    department_id    INT NOT NULL REFERENCES departments(id),
    default_priority priority_level NOT NULL DEFAULT 'normal',
    is_active        BOOLEAN NOT NULL DEFAULT TRUE
);

-- ---------- REPORTS ----------
CREATE TABLE reports (
    id             SERIAL PRIMARY KEY,
    user_id        INT NOT NULL REFERENCES users(id),        -- an account is required to report
    category_id    INT NOT NULL REFERENCES categories(id),
    department_id  INT NOT NULL REFERENCES departments(id),  -- copied from category at creation, can be reassigned
    assigned_to    INT REFERENCES users(id),                 -- operator handling it
    description    TEXT,
    latitude       NUMERIC(9,6) NOT NULL,
    longitude      NUMERIC(9,6) NOT NULL,
    address        VARCHAR(255),
    status         report_status NOT NULL DEFAULT 'submitted',
    priority       priority_level NOT NULL DEFAULT 'normal', -- set from category, admin can override
    is_anonymous   BOOLEAN NOT NULL DEFAULT FALSE,           -- hides identity from public view
    source         report_source NOT NULL DEFAULT 'mobile',
    rejection_reason TEXT,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    resolved_at    TIMESTAMPTZ,
    archived_at    TIMESTAMPTZ                               -- archived instead of deleted
);

-- ---------- PHOTOS ----------
-- Rule (enforce in business layer): 1 to 3 photos of kind 'report' per report.
CREATE TABLE report_photos (
    id           SERIAL PRIMARY KEY,
    report_id    INT NOT NULL REFERENCES reports(id) ON DELETE CASCADE,
    url          TEXT NOT NULL,
    kind         photo_kind NOT NULL DEFAULT 'report',
    uploaded_by  INT NOT NULL REFERENCES users(id),
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ---------- SUPPORTS ("me too" on an existing report) ----------
CREATE TABLE report_supports (
    report_id  INT NOT NULL REFERENCES reports(id) ON DELETE CASCADE,
    user_id    INT NOT NULL REFERENCES users(id),
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (report_id, user_id)                         -- one support per user per report
);

-- ---------- STATUS HISTORY (timeline + resolution-time statistics) ----------
CREATE TABLE status_history (
    id          SERIAL PRIMARY KEY,
    report_id   INT NOT NULL REFERENCES reports(id) ON DELETE CASCADE,
    old_status  report_status,
    new_status  report_status NOT NULL,
    changed_by  INT NOT NULL REFERENCES users(id),
    note        TEXT,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ---------- COMMENTS ----------
-- is_internal = TRUE: staff-only note. FALSE: public response the citizen sees.
CREATE TABLE comments (
    id          SERIAL PRIMARY KEY,
    report_id   INT NOT NULL REFERENCES reports(id) ON DELETE CASCADE,
    user_id     INT NOT NULL REFERENCES users(id),
    body        TEXT NOT NULL,
    is_internal BOOLEAN NOT NULL DEFAULT FALSE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ---------- NOTIFICATIONS (the "Notices" tab) ----------
CREATE TABLE notifications (
    id         SERIAL PRIMARY KEY,
    user_id    INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    report_id  INT REFERENCES reports(id) ON DELETE CASCADE,
    title      VARCHAR(150) NOT NULL,
    body       TEXT,
    is_read    BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ---------- PUSH TOKENS ----------
CREATE TABLE device_tokens (
    id         SERIAL PRIMARY KEY,
    user_id    INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token      TEXT NOT NULL UNIQUE,
    platform   device_platform NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ---------- AUDIT LOG (who changed what, and when) ----------
CREATE TABLE audit_log (
    id          SERIAL PRIMARY KEY,
    actor_id    INT REFERENCES users(id),
    action      VARCHAR(100) NOT NULL,      -- e.g. 'report.priority_changed', 'user.suspended'
    entity_type VARCHAR(50)  NOT NULL,
    entity_id   INT,
    details     JSONB,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ---------- INDEXES ----------
CREATE INDEX idx_reports_queue    ON reports (status, created_at) WHERE archived_at IS NULL;
CREATE INDEX idx_reports_dept     ON reports (department_id, status);
CREATE INDEX idx_reports_category ON reports (category_id);
CREATE INDEX idx_reports_user     ON reports (user_id);
CREATE INDEX idx_reports_geo      ON reports (latitude, longitude);   -- for "similar report nearby"
CREATE INDEX idx_notifs_user      ON notifications (user_id, is_read, created_at DESC);
CREATE INDEX idx_status_hist      ON status_history (report_id, created_at);

-- ---------- ADMIN QUEUE: oldest unresolved report first ----------
CREATE VIEW v_open_reports_queue AS
SELECT r.*,
       now() - r.created_at AS age
FROM reports r
WHERE r.status NOT IN ('resolved', 'rejected')
  AND r.archived_at IS NULL
ORDER BY r.created_at ASC;
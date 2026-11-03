create type user_role as enum ('citizen', 'operator', 'admin');

alter type user_role owner to postgres;

create type user_status as enum ('active', 'suspended');

alter type user_status owner to postgres;

create type report_status as enum ('submitted', 'in_review', 'assigned', 'in_progress', 'resolved', 'rejected');

alter type report_status owner to postgres;

create type priority_level as enum ('low', 'normal', 'high');

alter type priority_level owner to postgres;

create type report_source as enum ('mobile', 'web');

alter type report_source owner to postgres;

create type photo_kind as enum ('report', 'resolution');

alter type photo_kind owner to postgres;

create type device_platform as enum ('ios', 'android', 'web');

alter type device_platform owner to postgres;

create table departments
(
    id            serial
        primary key,
    name_bs       varchar(150)         not null,
    name_en       varchar(150)         not null,
    contact_email varchar(255),
    is_active     boolean default true not null
);

alter table departments
    owner to postgres;

create table users
(
    id            serial
        primary key,
    email         varchar(255)                                           not null
        unique,
    password_hash varchar(255)                                           not null,
    first_name    varchar(100)                                           not null,
    last_name     varchar(100)                                           not null,
    display_name  varchar(100),
    phone         varchar(30),
    avatar_url    text,
    role          user_role                default 'citizen'::user_role  not null,
    status        user_status              default 'active'::user_status not null,
    department_id integer
        references departments,
    language      char(2)                  default 'bs'::bpchar          not null
        constraint users_language_check
            check (language = ANY (ARRAY ['bs'::bpchar, 'en'::bpchar])),
    created_at    timestamp with time zone default now()                 not null,
    updated_at    timestamp with time zone default now()                 not null,
    deleted_at    timestamp with time zone,
    constraint users_check
        check ((role <> 'operator'::user_role) OR (department_id IS NOT NULL))
);

alter table users
    owner to postgres;

create table categories
(
    id               serial
        primary key,
    name_bs          varchar(100)                                    not null,
    name_en          varchar(100)                                    not null,
    icon             varchar(50),
    department_id    integer                                         not null
        references departments,
    default_priority priority_level default 'normal'::priority_level not null,
    is_active        boolean        default true                     not null
);

alter table categories
    owner to postgres;

create table reports
(
    id               serial
        primary key,
    user_id          integer                                                     not null
        references users,
    category_id      integer                                                     not null
        references categories,
    department_id    integer                                                     not null
        references departments,
    assigned_to      integer
        references users,
    description      text,
    latitude         numeric(9, 6)                                               not null,
    longitude        numeric(9, 6)                                               not null,
    address          varchar(255),
    status           report_status            default 'submitted'::report_status not null,
    priority         priority_level           default 'normal'::priority_level   not null,
    is_anonymous     boolean                  default false                      not null,
    source           report_source            default 'mobile'::report_source    not null,
    rejection_reason text,
    created_at       timestamp with time zone default now()                      not null,
    updated_at       timestamp with time zone default now()                      not null,
    resolved_at      timestamp with time zone,
    archived_at      timestamp with time zone
);

alter table reports
    owner to postgres;

create table report_photos
(
    id          serial
        primary key,
    report_id   integer                                               not null
        references reports
            on delete cascade,
    url         text                                                  not null,
    kind        photo_kind               default 'report'::photo_kind not null,
    uploaded_by integer                                               not null
        references users,
    created_at  timestamp with time zone default now()                not null
);

alter table report_photos
    owner to postgres;

create table report_supports
(
    report_id  integer                                not null
        references reports
            on delete cascade,
    user_id    integer                                not null
        references users,
    created_at timestamp with time zone default now() not null,
    primary key (report_id, user_id)
);

alter table report_supports
    owner to postgres;

create table status_history
(
    id         serial
        primary key,
    report_id  integer                                not null
        references reports
            on delete cascade,
    old_status report_status,
    new_status report_status                          not null,
    changed_by integer                                not null
        references users,
    note       text,
    created_at timestamp with time zone default now() not null
);

alter table status_history
    owner to postgres;

create table comments
(
    id          serial
        primary key,
    report_id   integer                                not null
        references reports
            on delete cascade,
    user_id     integer                                not null
        references users,
    body        text                                   not null,
    is_internal boolean                  default false not null,
    created_at  timestamp with time zone default now() not null
);

alter table comments
    owner to postgres;

create table notifications
(
    id         serial
        primary key,
    user_id    integer                                not null
        references users
            on delete cascade,
    report_id  integer
        references reports
            on delete cascade,
    title      varchar(150)                           not null,
    body       text,
    is_read    boolean                  default false not null,
    created_at timestamp with time zone default now() not null
);

alter table notifications
    owner to postgres;

create table device_tokens
(
    id         serial
        primary key,
    user_id    integer                                not null
        references users
            on delete cascade,
    token      text                                   not null
        unique,
    platform   device_platform                        not null,
    created_at timestamp with time zone default now() not null
);

alter table device_tokens
    owner to postgres;

create table audit_log
(
    id          serial
        primary key,
    actor_id    integer
        references users,
    action      varchar(100)                           not null,
    entity_type varchar(50)                            not null,
    entity_id   integer,
    details     jsonb,
    created_at  timestamp with time zone default now() not null
);

alter table audit_log
    owner to postgres;


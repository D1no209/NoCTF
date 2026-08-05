CREATE TABLE public.competition_events (
    id uuid PRIMARY KEY,
    competition_id uuid NOT NULL,
    kind text NOT NULL,
    occurred_at timestamptz NOT NULL
);

CREATE TABLE public.users (
    id uuid PRIMARY KEY,
    user_name text NOT NULL
);

CREATE SCHEMA wolverine_api;
CREATE SCHEMA wolverine_worker;
CREATE SCHEMA wolverine_runner;

CREATE TABLE wolverine_api.mt_doc_incoming_envelope (
    id uuid PRIMARY KEY,
    status text NOT NULL
);

CREATE TABLE wolverine_worker.mt_doc_outgoing_envelope (
    id uuid PRIMARY KEY,
    status text NOT NULL
);

CREATE TABLE wolverine_runner.wolverine_dead_letters (
    id uuid PRIMARY KEY,
    exception_type text NOT NULL
);

INSERT INTO public.competition_events VALUES
    ('00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000010', 'Started', '2026-08-06T00:00:00Z'),
    ('00000000-0000-0000-0000-000000000002', '00000000-0000-0000-0000-000000000010', 'FirstBlood', '2026-08-06T00:01:00Z');
INSERT INTO public.users VALUES
    ('00000000-0000-0000-0000-000000000020', 'recovery-user');
INSERT INTO wolverine_api.mt_doc_incoming_envelope VALUES
    ('00000000-0000-0000-0000-000000000030', 'Incoming');
INSERT INTO wolverine_worker.mt_doc_outgoing_envelope VALUES
    ('00000000-0000-0000-0000-000000000040', 'Scheduled');
INSERT INTO wolverine_runner.wolverine_dead_letters VALUES
    ('00000000-0000-0000-0000-000000000050', 'FixtureFailure');

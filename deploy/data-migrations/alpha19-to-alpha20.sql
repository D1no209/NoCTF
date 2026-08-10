\set ON_ERROR_STOP on

BEGIN;

DO $migration_preconditions$
DECLARE
    target_table text;
    target_count bigint;
BEGIN
    IF to_regnamespace('legacy_alpha19') IS NULL THEN
        RAISE EXCEPTION 'legacy_alpha19 schema is required';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM public."__EFMigrationsHistory"
        WHERE migration_id = '20260810002212_InitialBaseline'
    ) THEN
        RAISE EXCEPTION 'the Alpha.20 initial baseline must be applied before migrating data';
    END IF;

    FOREACH target_table IN ARRAY ARRAY[
        'files', 'notifications', 'data_exports', 'platform_settings', 'users',
        'account_tokens', 'challenges', 'competitions', 'challenge_attachments',
        'competition_challenges', 'competition_events', 'teams', 'challenge_flags',
        'gameplay_facts', 'patch_uploads', 'runtime_instances'
    ]
    LOOP
        EXECUTE format('SELECT count(*) FROM public.%I', target_table)
        INTO target_count;
        IF (target_table = 'platform_settings' AND target_count <> 1)
            OR (target_table <> 'platform_settings' AND target_count <> 0) THEN
            RAISE EXCEPTION 'target table public.% has unexpected baseline row count %',
                target_table,
                target_count;
        END IF;
    END LOOP;
END
$migration_preconditions$;

CREATE OR REPLACE FUNCTION pg_temp.copy_shared_columns(source_table text)
RETURNS bigint
LANGUAGE plpgsql
AS $copy_shared_columns$
DECLARE
    columns_sql text;
    insert_columns_sql text;
    select_columns_sql text;
    copied_rows bigint;
BEGIN
    SELECT string_agg(format('%I', target.column_name), ', ' ORDER BY target.ordinal_position)
    INTO columns_sql
    FROM information_schema.columns AS target
    INNER JOIN information_schema.columns AS source
        ON source.table_schema = 'legacy_alpha19'
        AND source.table_name = target.table_name
        AND source.column_name = target.column_name
    WHERE target.table_schema = 'public'
        AND target.table_name = source_table;

    IF columns_sql IS NULL THEN
        RAISE EXCEPTION 'no shared columns found for %', source_table;
    END IF;

    insert_columns_sql := columns_sql;
    select_columns_sql := columns_sql;
    IF source_table = 'competitions' THEN
        insert_columns_sql := insert_columns_sql || ', leaderboard_dirty';
        select_columns_sql := select_columns_sql || ', true';
    END IF;

    EXECUTE format(
        'INSERT INTO public.%1$I (%2$s) SELECT %3$s FROM legacy_alpha19.%1$I',
        source_table,
        insert_columns_sql,
        select_columns_sql);
    GET DIAGNOSTICS copied_rows = ROW_COUNT;
    RAISE NOTICE 'copied % rows into public.%', copied_rows, source_table;
    RETURN copied_rows;
END
$copy_shared_columns$;

SET LOCAL session_replication_role = replica;

DELETE FROM public.platform_settings;

SELECT pg_temp.copy_shared_columns('files');
SELECT pg_temp.copy_shared_columns('notifications');
SELECT pg_temp.copy_shared_columns('data_exports');
SELECT pg_temp.copy_shared_columns('platform_settings');
SELECT pg_temp.copy_shared_columns('users');
SELECT pg_temp.copy_shared_columns('account_tokens');
SELECT pg_temp.copy_shared_columns('challenges');
SELECT pg_temp.copy_shared_columns('competitions');
UPDATE public.competitions SET leaderboard_dirty = true;
SELECT pg_temp.copy_shared_columns('challenge_attachments');
SELECT pg_temp.copy_shared_columns('competition_challenges');
SELECT pg_temp.copy_shared_columns('competition_events');
SELECT pg_temp.copy_shared_columns('teams');
SELECT pg_temp.copy_shared_columns('challenge_flags');

INSERT INTO public.gameplay_facts (
    id,
    competition_id,
    competition_challenge_id,
    team_id,
    victim_team_id,
    actor_user_id,
    kind,
    occurred_at,
    reference_kind,
    reference_id,
    value,
    value_sha256,
    state,
    result,
    failure_code,
    updated_at)
SELECT
    submission.id,
    submission.competition_id,
    submission.competition_challenge_id,
    submission.team_id,
    scoring.victim_team_id,
    submission.submitted_by_user_id,
    submission.kind,
    submission.received_at,
    CASE WHEN submission.patch_upload_id IS NOT NULL THEN 0::smallint END,
    submission.patch_upload_id,
    submission.submitted_flag,
    submission.submitted_flag_sha256,
    CASE
        WHEN scoring.result = 4 THEN 4::smallint
        ELSE submission.evaluation_state
    END,
    CASE scoring.result
        WHEN 0 THEN 0::smallint
        WHEN 1 THEN 1::smallint
        WHEN 2 THEN 2::smallint
        WHEN 3 THEN 3::smallint
        WHEN 5 THEN 4::smallint
        ELSE NULL
    END,
    COALESCE(scoring.failure_code, submission.evaluation_failure_code),
    submission.evaluation_updated_at
FROM legacy_alpha19.submissions AS submission
LEFT JOIN legacy_alpha19.scoring_events AS scoring
    ON scoring.id = submission.current_scoring_event_id;

SELECT pg_temp.copy_shared_columns('patch_uploads');
SELECT pg_temp.copy_shared_columns('runtime_instances');

UPDATE public.runtime_instances AS runtime
SET gameplay_fact_id = legacy.submission_id
FROM legacy_alpha19.runtime_instances AS legacy
WHERE legacy.id = runtime.id
    AND legacy.submission_id IS NOT NULL;

UPDATE public.competition_events AS event
SET
    subject_id = CASE
        WHEN event.subject_type = 7 THEN scoring_subject.submission_id
        ELSE event.subject_id
    END,
    subject_type = CASE
        WHEN event.subject_type = 7 THEN 6::smallint
        WHEN event.subject_type >= 8 THEN event.subject_type - 1
        ELSE event.subject_type
    END,
    related_id = CASE
        WHEN event.related_type = 7 THEN scoring_related.submission_id
        ELSE event.related_id
    END,
    related_type = CASE
        WHEN event.related_type = 7 THEN 6::smallint
        WHEN event.related_type >= 8 THEN event.related_type - 1
        ELSE event.related_type
    END
FROM public.competition_events AS original
LEFT JOIN legacy_alpha19.scoring_events AS scoring_subject
    ON original.subject_type = 7
    AND scoring_subject.id = original.subject_id
LEFT JOIN legacy_alpha19.scoring_events AS scoring_related
    ON original.related_type = 7
    AND scoring_related.id = original.related_id
WHERE original.id = event.id;

WITH transformed_event_payloads AS (
    SELECT
        event.id,
        event.payload_json
            - 'submissionId'
            - 'submissionKind'
            - 'submissionState'
            - 'scoringEventId'
            - 'scoringEventKind'
            - 'scoringResult'
        || jsonb_strip_nulls(jsonb_build_object(
            'gameplayFactId', COALESCE(
                event.payload_json ->> 'submissionId',
                scoring.submission_id::text),
            'gameplayFactKind', CASE COALESCE(
                event.payload_json ->> 'submissionKind',
                CASE submission.kind
                    WHEN 0 THEN 'Flag'
                    WHEN 1 THEN 'Break'
                    WHEN 2 THEN 'Fix'
                    WHEN 3 THEN 'HintUnlock'
                    WHEN 4 THEN 'ManualAdjust'
                END)
                WHEN 'Flag' THEN 'FlagAttempt'
                WHEN 'Break' THEN 'BreakAttempt'
                WHEN 'Fix' THEN 'FixAttempt'
                WHEN 'HintUnlock' THEN 'HintUnlock'
                WHEN 'ManualAdjust' THEN 'ManualAdjustment'
            END,
            'gameplayFactState', CASE
                WHEN event.payload_json ->> 'scoringResult' = 'PlatformFailed'
                    THEN 'PlatformFailed'
                ELSE event.payload_json ->> 'submissionState'
            END,
            'gameplayFactResult', CASE event.payload_json ->> 'scoringResult'
                WHEN 'PlatformFailed' THEN NULL
                ELSE event.payload_json ->> 'scoringResult'
            END)) AS payload_json
    FROM public.competition_events AS event
    LEFT JOIN legacy_alpha19.scoring_events AS scoring
        ON scoring.id = NULLIF(event.payload_json ->> 'scoringEventId', '')::uuid
    LEFT JOIN legacy_alpha19.submissions AS submission
        ON submission.id = COALESCE(
            NULLIF(event.payload_json ->> 'submissionId', '')::uuid,
            scoring.submission_id)
)
UPDATE public.competition_events AS event
SET payload_json = transformed.payload_json
FROM transformed_event_payloads AS transformed
WHERE transformed.id = event.id;

UPDATE public.notifications AS notification
SET
    related_id = CASE
        WHEN notification.related_type = 7 THEN scoring.submission_id
        ELSE notification.related_id
    END,
    related_type = CASE
        WHEN notification.related_type = 7 THEN 6::smallint
        WHEN notification.related_type >= 8 THEN notification.related_type - 1
        ELSE notification.related_type
    END
FROM public.notifications AS original
LEFT JOIN legacy_alpha19.scoring_events AS scoring
    ON original.related_type = 7
    AND scoring.id = original.related_id
WHERE original.id = notification.id;

UPDATE public.notifications
SET content_json =
    content_json
        - 'submissionId'
        - 'scoringEventId'
        - 'submittedByUserId'
    || jsonb_strip_nulls(jsonb_build_object(
        'gameplayFactId', content_json ->> 'submissionId',
        'actorUserId', content_json ->> 'submittedByUserId'))
WHERE content_json ?| ARRAY['submissionId', 'scoringEventId', 'submittedByUserId'];

SET LOCAL session_replication_role = origin;

DO $migration_validation$
DECLARE
    table_name text;
    source_count bigint;
    target_count bigint;
BEGIN
    FOREACH table_name IN ARRAY ARRAY[
        'files', 'notifications', 'data_exports', 'platform_settings', 'users',
        'account_tokens', 'challenges', 'competitions', 'challenge_attachments',
        'competition_challenges', 'competition_events', 'teams', 'challenge_flags',
        'patch_uploads', 'runtime_instances'
    ]
    LOOP
        EXECUTE format('SELECT count(*) FROM legacy_alpha19.%I', table_name)
        INTO source_count;
        EXECUTE format('SELECT count(*) FROM public.%I', table_name)
        INTO target_count;
        IF source_count <> target_count THEN
            RAISE EXCEPTION 'row count mismatch for %: source %, target %',
                table_name,
                source_count,
                target_count;
        END IF;
    END LOOP;

    SELECT count(*) INTO source_count FROM legacy_alpha19.submissions;
    SELECT count(*) INTO target_count FROM public.gameplay_facts;
    IF source_count <> target_count THEN
        RAISE EXCEPTION 'row count mismatch for gameplay_facts: source %, target %',
            source_count,
            target_count;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM public.gameplay_facts AS fact
        LEFT JOIN public.competitions AS competition ON competition.id = fact.competition_id
        LEFT JOIN public.competition_challenges AS challenge
            ON challenge.id = fact.competition_challenge_id
        LEFT JOIN public.teams AS team ON team.id = fact.team_id
        LEFT JOIN public.users AS actor ON actor.id = fact.actor_user_id
        WHERE competition.id IS NULL
            OR challenge.id IS NULL
            OR (fact.team_id IS NOT NULL AND team.id IS NULL)
            OR (fact.actor_user_id IS NOT NULL AND actor.id IS NULL)
    ) THEN
        RAISE EXCEPTION 'gameplay_facts contains orphaned references';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM public.competition_events
        WHERE subject_type NOT BETWEEN 0 AND 12
            OR (related_type IS NOT NULL AND related_type NOT BETWEEN 0 AND 12)
            OR payload_json ?| ARRAY[
                'submissionId', 'submissionKind', 'submissionState',
                'scoringEventId', 'scoringEventKind', 'scoringResult']
    ) THEN
        RAISE EXCEPTION 'competition event reference or payload migration is incomplete';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM public.competition_events AS event
        LEFT JOIN public.gameplay_facts AS subject_fact
            ON event.subject_type = 6 AND subject_fact.id = event.subject_id
        LEFT JOIN public.runtime_instances AS subject_runtime
            ON event.subject_type = 7 AND subject_runtime.id = event.subject_id
        LEFT JOIN public.gameplay_facts AS related_fact
            ON event.related_type = 6 AND related_fact.id = event.related_id
        LEFT JOIN public.runtime_instances AS related_runtime
            ON event.related_type = 7 AND related_runtime.id = event.related_id
        WHERE (event.subject_type = 6 AND subject_fact.id IS NULL)
            OR (event.subject_type = 7 AND subject_runtime.id IS NULL)
            OR (event.related_type = 6 AND related_fact.id IS NULL)
            OR (event.related_type = 7 AND related_runtime.id IS NULL)
    ) THEN
        RAISE EXCEPTION 'competition event gameplay fact or runtime reference is orphaned';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM public.runtime_instances AS runtime
        LEFT JOIN public.gameplay_facts AS fact ON fact.id = runtime.gameplay_fact_id
        WHERE runtime.gameplay_fact_id IS NOT NULL AND fact.id IS NULL
    ) THEN
        RAISE EXCEPTION 'runtime instance gameplay fact reference is orphaned';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM public.notifications
        WHERE (related_type IS NOT NULL AND related_type NOT BETWEEN 0 AND 12)
            OR content_json ?| ARRAY['submissionId', 'scoringEventId', 'submittedByUserId']
    ) THEN
        RAISE EXCEPTION 'notification reference or payload migration is incomplete';
    END IF;
END
$migration_validation$;

COMMIT;

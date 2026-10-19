SELECT jsonb_build_object(
  'utc', clock_timestamp(),
  'database', (SELECT row_to_json(d) FROM (
    SELECT numbackends, xact_commit, xact_rollback, blks_read, blks_hit,
           tup_returned, tup_fetched, tup_inserted, tup_updated, tup_deleted,
           conflicts, temp_files, temp_bytes, deadlocks, blk_read_time, blk_write_time
    FROM pg_stat_database WHERE datname = current_database()
  ) d),
  'activity', COALESCE((SELECT json_agg(a) FROM (
    SELECT pid, state, wait_event_type, wait_event, query_id::text,
           EXTRACT(epoch FROM clock_timestamp() - xact_start) * 1000 AS transaction_ms,
           EXTRACT(epoch FROM clock_timestamp() - query_start) * 1000 AS query_ms,
           pg_blocking_pids(pid) AS blocking_pids
    FROM pg_stat_activity
    WHERE datname = current_database() AND pid <> pg_backend_pid()
  ) a), '[]'::json),
  'locks', COALESCE((SELECT json_agg(l) FROM (
    SELECT locktype, mode, granted, relation::regclass::text AS relation, count(*) AS count
    FROM pg_locks WHERE database = (SELECT oid FROM pg_database WHERE datname = current_database())
    GROUP BY locktype, mode, granted, relation
  ) l), '[]'::json),
  'statements', COALESCE((SELECT json_agg(s) FROM (
    SELECT queryid::text, calls, plans, total_plan_time, mean_plan_time, total_exec_time, min_exec_time,
           max_exec_time, mean_exec_time, rows, shared_blks_hit, shared_blks_read,
           shared_blks_dirtied, shared_blks_written, temp_blks_read, temp_blks_written,
           shared_blk_read_time, shared_blk_write_time
    FROM pg_stat_statements WHERE dbid = (SELECT oid FROM pg_database WHERE datname = current_database())
    ORDER BY total_exec_time + total_plan_time DESC LIMIT 50
  ) s), '[]'::json)
);

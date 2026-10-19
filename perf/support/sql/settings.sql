SELECT jsonb_build_object(
  'maxConnections', current_setting('max_connections')::int,
  'superuserReservedConnections', current_setting('superuser_reserved_connections')::int,
  'sharedBuffers', current_setting('shared_buffers'),
  'workMemory', current_setting('work_mem'),
  'ioTiming', current_setting('track_io_timing'),
  'statementTracking', current_setting('pg_stat_statements.track'),
  'planningTracking', current_setting('pg_stat_statements.track_planning')
);

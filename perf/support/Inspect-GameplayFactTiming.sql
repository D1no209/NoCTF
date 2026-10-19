SELECT coalesce(json_agg(row_to_json(summary)), '[]')
FROM (
    SELECT state, result, count(*) AS facts,
        percentile_cont(0.5) WITHIN GROUP (ORDER BY (updated_at - occurred_at) / 10000.0) AS p50_ms,
        percentile_cont(0.95) WITHIN GROUP (ORDER BY (updated_at - occurred_at) / 10000.0) AS p95_ms,
        max((updated_at - occurred_at) / 10000.0) AS maximum_ms,
        count(*) FILTER (WHERE updated_at - occurred_at > 300000000) AS over_thirty_seconds
    FROM gameplay_facts
    WHERE kind = 0
    GROUP BY state, result
) summary;

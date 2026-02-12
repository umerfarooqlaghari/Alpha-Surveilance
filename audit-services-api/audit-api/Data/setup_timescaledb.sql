-- ALPHA SURVEILLANCE: TimescaleDB Setup Script
-- -------------------------------------------

-- 1. Enable the TimescaleDB extension (Run as superuser)
CREATE EXTENSION IF NOT EXISTS timescaledb;

-- 2. Create the Hypertable
-- In TimescaleDB, a 'Hypertable' is partitioned by time.
-- This allows us to handle billions of logs without a performance drop.
-- NOTE: Run this AFTER your EF Core migrations have created the table.
SELECT create_hypertable('"AuditLogs"', 'Timestamp');

-- 3. (Optional) Setup Compression to save disk space after 30 days
ALTER TABLE '"AuditLogs"' SET (
  timescaledb.compress,
  timescaledb.compress_segmentby = '"TenantId"'
);

SELECT add_compression_policy('"AuditLogs"', INTERVAL '30 days');

-- Apply only to the user's selected existing CloudBase PostgreSQL environment.
-- No existing application schema, table, role or policy is changed.
CREATE SCHEMA IF NOT EXISTS wechat_workspace;
REVOKE ALL ON SCHEMA wechat_workspace FROM PUBLIC;
CREATE TABLE IF NOT EXISTS wechat_workspace.queue (
  id text PRIMARY KEY CHECK (id = 'agent' OR id ~ '^cmd_[a-f0-9]{64}$'),
  version bigint NOT NULL DEFAULT 1,
  data jsonb NOT NULL,
  CHECK (jsonb_typeof(data) = 'object')
);
ALTER TABLE wechat_workspace.queue ENABLE ROW LEVEL SECURITY;
REVOKE ALL ON TABLE wechat_workspace.queue FROM PUBLIC;
CREATE INDEX IF NOT EXISTS wechat_workspace_queue_state_created
  ON wechat_workspace.queue ((data->>'state'), ((data->>'createdAt')::bigint));
CREATE INDEX IF NOT EXISTS wechat_workspace_queue_purge
  ON wechat_workspace.queue (((data->>'purgeAfter')::bigint));
CREATE INDEX IF NOT EXISTS wechat_workspace_queue_payload
  ON wechat_workspace.queue (((data->>'payloadUntil')::bigint))
  WHERE data->>'payloadUntil' IS NOT NULL;
DO $bridge_permissions$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
    REVOKE ALL ON SCHEMA wechat_workspace FROM anon;
    REVOKE ALL ON TABLE wechat_workspace.queue FROM anon;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
    REVOKE ALL ON SCHEMA wechat_workspace FROM authenticated;
    REVOKE ALL ON TABLE wechat_workspace.queue FROM authenticated;
  END IF;
END
$bridge_permissions$;

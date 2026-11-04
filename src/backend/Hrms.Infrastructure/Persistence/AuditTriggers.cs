namespace Hrms.Infrastructure.Persistence;

/// <summary>Database-level append-only enforcement for audit tables (in addition to SaveChanges guard and role grants).</summary>
public static class AuditTriggers
{
    public const string Create = """
        CREATE OR REPLACE FUNCTION audit.reject_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            RAISE EXCEPTION 'audit records are append-only (% on %)', TG_OP, TG_TABLE_NAME USING ERRCODE = '42501';
        END $$;
        CREATE TRIGGER trg_audit_entries_append_only BEFORE UPDATE OR DELETE ON audit.audit_entries FOR EACH ROW EXECUTE FUNCTION audit.reject_mutation();
        CREATE TRIGGER trg_security_events_append_only BEFORE UPDATE OR DELETE ON audit.security_events FOR EACH ROW EXECUTE FUNCTION audit.reject_mutation();
        CREATE TRIGGER trg_audit_seals_append_only BEFORE UPDATE OR DELETE ON audit.audit_seals FOR EACH ROW EXECUTE FUNCTION audit.reject_mutation();
        CREATE TRIGGER trg_audit_entries_no_truncate BEFORE TRUNCATE ON audit.audit_entries FOR EACH STATEMENT EXECUTE FUNCTION audit.reject_mutation();
        CREATE TRIGGER trg_security_events_no_truncate BEFORE TRUNCATE ON audit.security_events FOR EACH STATEMENT EXECUTE FUNCTION audit.reject_mutation();
        """;

    public const string Drop = "DROP FUNCTION IF EXISTS audit.reject_mutation() CASCADE;";
}

-- Destination must be an isolated, newly migrated database. psql --single-transaction.
\set ON_ERROR_STOP on
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM emergency.emergency_incidents) OR EXISTS (SELECT 1 FROM emergency.outbox_messages) THEN
        RAISE EXCEPTION 'Import rejected: destination already contains incidents/outbox';
    END IF;
END $$;
CREATE TEMP TABLE import_fall_types (LIKE emergency.fall_types INCLUDING DEFAULTS);
\copy import_fall_types (fall_type_id, name, description, severity_level) FROM 'fall_types.csv' WITH (FORMAT csv, HEADER true, ENCODING 'UTF8')
INSERT INTO emergency.fall_types (fall_type_id, name, description, severity_level)
SELECT fall_type_id, name, description, severity_level FROM import_fall_types
ON CONFLICT (fall_type_id) DO UPDATE SET name = excluded.name, description = excluded.description, severity_level = excluded.severity_level;
\copy emergency.emergency_incidents (emergency_incident_id, incident_key, device_id, patient_id, fall_type_id, status, opened_at, last_signal_at, cancelled_at, resolved_at, closed_at, closed_by_user_id, ai_confidence_score, latitude, longitude, address, cancellation_reason, final_observation, last_source_payload) FROM 'emergency_incidents.csv' WITH (FORMAT csv, HEADER true, ENCODING 'UTF8')
\copy emergency.outbox_messages (emergency_outbox_message_id, type, payload, occurred_on, processed_on, error, retry_count) FROM 'outbox_messages.csv' WITH (FORMAT csv, HEADER true, ENCODING 'UTF8')
SELECT setval(pg_get_serial_sequence('emergency.fall_types', 'fall_type_id'), COALESCE(MAX(fall_type_id), 1), COUNT(*) > 0) FROM emergency.fall_types;
SELECT setval(pg_get_serial_sequence('emergency.emergency_incidents', 'emergency_incident_id'), COALESCE(MAX(emergency_incident_id), 1), COUNT(*) > 0) FROM emergency.emergency_incidents;
SELECT setval(pg_get_serial_sequence('emergency.outbox_messages', 'emergency_outbox_message_id'), COALESCE(MAX(emergency_outbox_message_id), 1), COUNT(*) > 0) FROM emergency.outbox_messages;

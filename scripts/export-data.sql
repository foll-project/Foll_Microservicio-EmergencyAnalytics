-- Run psql from the backup directory with all emergency writers stopped.
\set ON_ERROR_STOP on
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
\copy emergency.fall_types (fall_type_id, name, description, severity_level) TO 'fall_types.csv' WITH (FORMAT csv, HEADER true, ENCODING 'UTF8')
\copy emergency.emergency_incidents (emergency_incident_id, incident_key, device_id, patient_id, fall_type_id, status, opened_at, last_signal_at, cancelled_at, resolved_at, closed_at, closed_by_user_id, ai_confidence_score, latitude, longitude, address, cancellation_reason, final_observation, last_source_payload) TO 'emergency_incidents.csv' WITH (FORMAT csv, HEADER true, ENCODING 'UTF8')
\copy emergency.outbox_messages (emergency_outbox_message_id, type, payload, occurred_on, processed_on, error, retry_count) TO 'outbox_messages.csv' WITH (FORMAT csv, HEADER true, ENCODING 'UTF8')
COMMIT;

SELECT 'fall_types' AS entity, count(*) AS total FROM emergency.fall_types
UNION ALL SELECT 'emergency_incidents', count(*) FROM emergency.emergency_incidents
UNION ALL SELECT 'outbox_messages', count(*) FROM emergency.outbox_messages;
SELECT status, count(*) FROM emergency.emergency_incidents GROUP BY status ORDER BY status;
SELECT count(*) AS pending_outbox FROM emergency.outbox_messages WHERE processed_on IS NULL;
SELECT count(*) AS orphan_fall_types FROM emergency.emergency_incidents i
LEFT JOIN emergency.fall_types f USING (fall_type_id) WHERE f.fall_type_id IS NULL;
SELECT device_id, count(*) FROM emergency.emergency_incidents WHERE status = 'Open'
GROUP BY device_id HAVING count(*) > 1;
SELECT min(opened_at), max(opened_at), count(DISTINCT incident_key) FROM emergency.emergency_incidents;

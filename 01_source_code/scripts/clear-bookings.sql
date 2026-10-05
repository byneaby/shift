BEGIN;
DELETE FROM booking_computers;
DELETE FROM bookings;
UPDATE computers SET "Status" = 'Free', "UpdatedAt" = NOW()
WHERE "Status" = 'Reserved' AND "CurrentSessionId" IS NULL;
COMMIT;

SELECT COUNT(*) AS bookings_left FROM bookings;
SELECT COUNT(*) AS reserved_left FROM computers WHERE "Status" = 'Reserved';

-- Financial reset (safe): money + reports from zero.
-- PRESERVES:
--   - gaming_sessions (all, especially Active/Paused)
--   - computers.CurrentSessionId
--   - customers.Balance, BonusBalance, TimeBankMinutes
--   - customer_zone_time_banks, customer_packages
--   - products.StockQty (bar inventory)
--
-- CLEARS:
--   - cash: receipts, payments, shifts, sequences
--   - bar sales history + inventory_movements (not stock)
--   - wallet/time ledgers (history only)
--   - bookings, payroll, case history
--   - session_history for COMPLETED sessions only
--   - customer stats used in reports (TotalSpent, VisitCount, TotalMinutesPlayed)

BEGIN;

-- Snapshot before (for log)
CREATE TEMP TABLE _reset_before AS
SELECT
  (SELECT COUNT(*) FROM gaming_sessions WHERE "Status" IN ('Active', 'Paused')) AS active_sessions,
  (SELECT COUNT(*) FROM computers WHERE "CurrentSessionId" IS NOT NULL) AS busy_pcs,
  (SELECT COALESCE(SUM("Balance"), 0) FROM customers WHERE "IsActive" = true) AS client_balance,
  (SELECT COALESCE(SUM("StockQty"), 0) FROM products) AS bar_stock;

-- 1) Cash & receipts
DELETE FROM payments;
DELETE FROM receipt_items;
DELETE FROM receipts;
DELETE FROM cash_movements;
DELETE FROM cash_shifts;
DELETE FROM document_sequences;

-- 2) Bar history (keep product catalog + StockQty)
DELETE FROM bar_order_items;
DELETE FROM bar_orders;
DELETE FROM inventory_movements;

-- 3) Wallet/time ledgers (keep cached balances on customers)
DELETE FROM customer_balance_transactions;
DELETE FROM customer_time_bank_transactions;

-- 4) Bookings
DELETE FROM booking_computers;
UPDATE computers
SET "Status" = 'Free', "UpdatedAt" = NOW()
WHERE "Status" = 'Reserved' AND "CurrentSessionId" IS NULL;
DELETE FROM bookings;

-- 5) Payroll
DELETE FROM payroll_accruals;
DELETE FROM work_shifts;

-- 6) Case / engagement history
DELETE FROM case_user_rewards;
DELETE FROM case_openings;
DELETE FROM case_key_ledger;

-- 7) Old sessions only (keep Active/Paused + PC links; cascades session_history)
DELETE FROM gaming_sessions
WHERE "Status" NOT IN ('Active', 'Paused');

-- 8) Zero report stats on customers (NOT balances)
UPDATE customers SET
  "TotalSpent" = 0,
  "VisitCount" = 0,
  "TotalMinutesPlayed" = 0;

-- 9) Clear dangling prepay refs on bookings (already deleted) — nothing needed

COMMIT;

-- Verify after reset
SELECT 'active_sessions' AS metric, COUNT(*)::text AS value
FROM gaming_sessions WHERE "Status" IN ('Active', 'Paused')
UNION ALL
SELECT 'busy_pcs', COUNT(*)::text FROM computers WHERE "CurrentSessionId" IS NOT NULL
UNION ALL
SELECT 'receipts_left', COUNT(*)::text FROM receipts
UNION ALL
SELECT 'bar_orders_left', COUNT(*)::text FROM bar_orders
UNION ALL
SELECT 'balance_tx_left', COUNT(*)::text FROM customer_balance_transactions
UNION ALL
SELECT 'bar_stock_qty', COALESCE(SUM("StockQty")::text, '0') FROM products
UNION ALL
SELECT 'client_balance_kzt', COALESCE(ROUND(SUM("Balance")::numeric, 2)::text, '0') FROM customers WHERE "IsActive" = true;

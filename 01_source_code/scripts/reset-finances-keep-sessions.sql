-- Financial reset: reports/cash history from zero.
-- KEEPS: active gaming sessions, customer balances, bar stock (products.StockQty).
-- Run preflight first; then execute block below.

-- Preflight (read-only)
SELECT 'active_sessions' AS metric, COUNT(*)::text AS value
FROM gaming_sessions WHERE "Status" IN ('Active', 'Paused')
UNION ALL
SELECT 'busy_pcs', COUNT(*)::text FROM computers WHERE "CurrentSessionId" IS NOT NULL
UNION ALL
SELECT 'open_cash_shifts', COUNT(*)::text FROM cash_shifts WHERE "Status" = 'Open'
UNION ALL
SELECT 'receipts', COUNT(*)::text FROM receipts
UNION ALL
SELECT 'bar_orders', COUNT(*)::text FROM bar_orders
UNION ALL
SELECT 'bar_stock_qty', COALESCE(SUM("StockQty")::text, '0') FROM products
UNION ALL
SELECT 'client_balance_kzt', COALESCE(ROUND(SUM("Balance")::numeric, 2)::text, '0') FROM customers WHERE "IsActive" = true;

-- Post-reset verification
SELECT gs."Id", gs."Status", c."WindowsName", c."CurrentSessionId" = gs."Id" AS pc_linked
FROM gaming_sessions gs
JOIN computers c ON c."CurrentSessionId" = gs."Id"
WHERE gs."Status" IN ('Active', 'Paused')
ORDER BY c."WindowsName";

SELECT COUNT(*) AS orphaned_busy_pc
FROM computers c
LEFT JOIN gaming_sessions gs ON gs."Id" = c."CurrentSessionId"
WHERE c."CurrentSessionId" IS NOT NULL
  AND (gs."Id" IS NULL OR gs."Status" NOT IN ('Active', 'Paused'));

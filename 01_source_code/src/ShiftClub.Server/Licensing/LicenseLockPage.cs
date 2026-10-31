using System.Net;

namespace ShiftClub.Server.Licensing;

public static class LicenseLockPage
{
    public static string Html(string message)
    {
        var safe = WebUtility.HtmlEncode(message);
        return $$"""
            <!DOCTYPE html>
            <html lang="ru">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>SHIFT — лицензия</title>
              <style>
                body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: #121612; color: #e7f0e2; font: 17px/1.45 "Segoe UI", sans-serif; }
                main { width: min(520px, calc(100% - 32px)); }
                h1 { font-size: 28px; margin-bottom: 8px; }
                h1 span { color: #c6f54a; }
                p { color: #c5d4be; }
                form { display: grid; gap: 10px; margin-top: 18px; }
                input, button { font: inherit; padding: 12px 14px; border-radius: 12px; }
                input { border: 1px solid #3a4a36; background: #0e130e; color: inherit; }
                button { border: 0; background: #c6f54a; color: #15200c; font-weight: 700; cursor: pointer; }
                #out { min-height: 1.2em; }
              </style>
            </head>
            <body>
              <main>
                <h1>SHIFT <span>остановлен</span></h1>
                <p>{{safe}}</p>
                <p>Касса, новые сессии и клиентские ПК не работают. База клуба на месте. Вставьте новый ключ — зал включится без переустановки.</p>
                <form id="form">
                  <input id="key" name="key" autocomplete="off" placeholder="SHIFT-XXXX-XXXX-XXXX" required />
                  <button type="submit">Включить сервер</button>
                  <p id="out"></p>
                </form>
              </main>
              <script>
                document.getElementById("form").addEventListener("submit", async (e) => {
                  e.preventDefault();
                  const out = document.getElementById("out");
                  out.textContent = "Проверяю ключ…";
                  const res = await fetch("/api/license/activate", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ key: document.getElementById("key").value })
                  });
                  const data = await res.json().catch(() => null);
                  if (res.ok && data && data.success) {
                    location.assign("/");
                    return;
                  }
                  out.textContent = (data && data.message) || "Ключ не принят.";
                });
              </script>
            </body>
            </html>
            """;
    }
}

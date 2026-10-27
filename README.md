# 桌面工作檯總管

Windows 桌面浮窗，用來集中加入、開啟與啟停本機工作檯。沿用原有三欄介面，初始工作檯清單為空。

## 開始使用

1. 使用 Windows 10／11（64 位元），安裝 Python 3.10 以上，並包含 pip。使用可寫入的資料夾。
2. 下載本儲存庫並解壓縮，雙擊 `Setup.cmd`。它會建立專案自己的 `.venv`、安裝 psutil 並使用 Windows 內建 .NET Framework 編譯桌面程式。
3. 雙擊 `Start-hub.cmd` 開啟總管，點「＋」加入工作檯的名稱、CMD／BAT 啟動檔及本機網址；停止檔可選填。

點工作檯名稱會啟動並開啟瀏覽器；右側開關控制服務。關閉總管不會關閉已啟動的工作檯。

## 初始狀態與個人設定

- 不含任何既有工作檯、私人路徑、憑證、對話、快取或手機版服務。
- `settings.example.json` 是空白範本；首次安裝會建立 `settings.json`，個人設定與 `data/` 不納入 Git。
- AI 額度與 Token 監控預設關閉，畫面顯示未啟用。若要使用，關閉總管後將 `settings.json` 的 `monitor_ai` 改為 `true`。它會讀取本機 Codex／Claude 工作階段紀錄，並透過既有登入狀態查詢額度；需要已安裝並自行登入的官方工具。未登入或無法查詢時顯示未知，不會代登入或送出模型提示詞。
- 啟用 AI 監控後，Codex CLI 必須位於 PATH；帳號與紀錄格式改變可能影響監控。
- 若未提供停止檔，僅停止經工作目錄核對的該工作檯服務。會寫入資料或執行生成的服務，請提供自己的安全停止檔。

修改程式後可執行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` 重新編譯。執行 `.venv\Scripts\python.exe test_initial.py` 檢查空白狀態與加入設定。

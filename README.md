# BattleCode# BattleCode 線上程式對戰平台

這是一個結合 **ASP.NET MVC** 與 **SignalR** 的即時線上程式對戰平台。系統整合了 AI 服務，提供自動出題、即時判題（Judge）以及賽後 AI 分析功能，讓使用者能透過 1 對 1 對戰或個人練習模式精進程式能力。

## 🌟 功能介紹

*   **即時對戰 (1v1 Battle)**：
    *   透過 SignalR 進行即時配對。
    *   同步對戰狀態（倒數、切換題目、對手進度通知）。
    *   雙方皆答對或時間到自動切換下一題。
*   **AI 輔助系統**：
    *   **AI 出題**：根據選擇的難度與語言自動生成題目。
    *   **AI 判題**：自動執行測試案例驗證程式碼正確性。
    *   **AI 分析與提示**：在賽後提供程式碼優化建議，對戰中可請求提示。
*   **個人練習模式**：單人進行題目練習，累積積分。
*   **使用者系統**：
    *   Google 快速登入 / 一般帳號註冊。
    *   個人戰績儀表板 (Dashboard) 與錯題分析。
    *   排行榜 (Leaderboard)。

## 📂 專案結構與檔案說明

此專案基於 ASP.NET MVC 架構，主要邏輯位於以下核心檔案中：

### 1. Controllers (控制器)

*   **`HomeController.cs`**
    *   **核心功能**：處理首頁、使用者認證（登入/註冊/Google第三方登入）、個人頁面管理。
    *   **詳細職責**：
        *   `GoogleLogin`: 處理 Google ID Token 驗證 (需設定 Client ID)。
        *   `Person`: 使用者個人資料修改與頭像上傳。
        *   `Dashboard` / `AIAnalysis`: 提供圖表統計數據與錯題分析。
*   **`MatchController.cs`**
    *   **核心功能**：處理 1 對 1 對戰的主要流程。
    *   **詳細職責**：
        *   `Start`: 尋找等待中的對手或建立新局。
        *   `Battle`: 對戰主畫面，載入題目與對手資訊。
        *   `SubmitCode`: 接收程式碼提交，呼叫 AI 判題服務，並回傳結果。
        *   `Result`: 計算賽後分數 (Rating) 與顯示勝負結果。
*   **`PracticeController.cs`**
    *   **核心功能**：個人練習模式邏輯。
    *   **詳細職責**：建立練習賽局，不影響排位積分（但會累積經驗值），流程與對戰類似但不需等待對手。

### 2. Hubs (SignalR 即時通訊)

*   **`BattleHub.cs`**
    *   **核心功能**：管理所有即時連線與雙向溝通。
    *   **詳細職責**：
        *   **配對佇列** (`JoinQueue`): 管理等待配對的玩家清單。
        *   **房間管理** (`match_{id}`): 將配對成功的玩家加入群組，廣播遊戲事件（如 `startCountdown`, `nextProblem`）。
        *   **斷線處理** (`OnDisconnected`): 處理玩家中途離線的勝負判定或資源釋放。

### 3. Services (服務層)

*   **`AIAnalysisService`** / **`AiJudgerService`**: 封裝與 AI 模型（如 OpenAI）溝通的邏輯，負責生成題目、判斷程式碼輸出與提供建議。

---

## ⚙️ 安裝與執行方式

### 前置需求
*   Visual Studio 2019 或更新版本 (支援 .NET Framework 4.7.2+)。
*   SQL Server LocalDB (通常隨 Visual Studio 安裝)。

### 步驟
1.  **開啟專案**：
    *   找到資料夾中的 `BattleCode.sln` 檔案。
    *   點擊兩下使用 Visual Studio 開啟。

2.  **還原套件**：
    *   在 Visual Studio 中，對著「方案總管」的專案按右鍵，選擇 **「還原 NuGet 套件」** 以下載所需函式庫 (SignalR, EntityFramework 等)。

3.  **設定 API Keys**：
    *   **重要**：本專案使用 Google 登入與 AI 服務，請務必更新以下設定：
    *   開啟 `Controllers/HomeController.cs`，搜尋 `GoogleJsonWebSignature`，將 `Audience` 替換為您的 **Google Client ID**：
        ```csharp
        // 在 HomeController.cs 中
        Audience = new[] { "YOUR_GOOGLE_CLIENT_ID" } 
        ```
    *   確認 AI 服務的 API Key 是否已在 `Web.config` 或環境變數中設定正確。

4.  **建立資料庫**：
    *   專案使用 Entity Framework Code First 或 Model First。第一次執行時，系統應會自動建立 LocalDB 資料庫 (`App_Data` 資料夾下)。

5.  **執行專案**：
    *   按下 **F5** 或點擊上方的 **IIS Express (Google Chrome)** 按鈕。
    *   瀏覽器將自動開啟首頁，即可開始測試。

## 🔑 變數取代說明 (Configuration)

為了安全起見，請勿將真實的金鑰 (Keys) 上傳至版本控制系統。建議在 `Web.config` 的 `<appSettings>` 區段設定以下變數：

| 變數名稱 | 說明 | 檔案位置範例 |
| :--- | :--- | :--- |
| `GoogleClientId` | Google OAuth 用戶端 ID | `HomeController.cs` |
| `OpenAiApiKey` | AI 服務金鑰 | `Services/AiJudgerService.cs` (或其他 Service) |
| `ConnectionStrings` | 資料庫連線字串 | `Web.config` |

---
*Happy Coding & Battling!*

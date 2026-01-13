using BattleCode.Models;
using Newtonsoft.Json;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace BattleCode.Services
{
    public class AiJudgerService
    {

            // 建議改成從設定檔注入
            private readonly string _apiKey = ""; // 自己key
            private readonly string _assistantId = ""; // 自己key

        // 呼叫 OpenAI AI 助理，判斷使用者提交的程式碼是否正確
        public async Task<JudgeResult> JudgeCodeAsync(string code, Problems problem, string language)
        {
            var client = new HttpClient();// 建立 HttpClient 物件用來呼叫 OpenAI API
            // 設定 API 金鑰在 Http Header 裡的 Authorization 欄位
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            // 建立給 AI 的提示詞（Prompt）
            // 告訴 AI 你是批改程式的助理，並要它輸出符合特定 JSON 格式的結果
            // 並且附上題目說明、輸入輸出格式、範例，還有使用者程式碼
            string prompt = $@"
你是一位幫忙批改程式的 AI 助理，請幫我判斷下方使用者程式碼是否正確，並請回傳以下 JSON 格式（務必只輸出 JSON 並符合格式）：

{{
  ""IsCorrect"": true 或 false,
  ""Result"": ""Correct"" 或 ""Wrong"" 或 ""Error"" 或 ""Timeout"",
  ""Output"": ""使用者程式的輸出結果"",
  ""ErrorMessage"": ""如果有錯誤，請寫錯誤內容，否則請為空字串"",
  ""ExecutionTimeMs"": 整數毫秒數,
  ""Analysis"": ""請給予簡短中文說明，限 200 字內""
}}

題目標題：{problem.Title}

題目描述：
{problem.Description}

輸入格式：
{problem.InputFormat}

輸出格式：
{problem.OutputFormat}

範例輸入：
{problem.SampleInputs}

範例輸出：
{problem.SampleOutputs}

使用語言：{language}

使用者程式碼：
{code}
";
            // 封裝送給 OpenAI Chat Completion API 的 request body
            var requestBody = new
            {
                model = "gpt-4o-mini",// 使用的模型
                messages = new[]
                {
                    // 系統角色：定義助理身份與風格
                    new { role = "system", content = "你是一位幫忙批改程式的 AI 助理。" },
                    // 使用者角色：實際給 AI 的指令及資料
                    new { role = "user", content = prompt }
                }
            };
            // 將 requestBody 序列化成 JSON，並包成 HttpContent，Content-Type 是 application/json
            var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
            var responseString = await response.Content.ReadAsStringAsync();

            dynamic aiResponse = JsonConvert.DeserializeObject(responseString);
            string jsonRaw = aiResponse.choices[0].message.content;
            // 清理掉多餘的 markdown ```json 或 ``` 符號，保留純 JSON 字串
            string jsonString = jsonRaw
                .Replace("```json", "")
                .Replace("```", "")
                .Trim();

            try
            {
                return JsonConvert.DeserializeObject<JudgeResult>(jsonString);
            }
            catch (Exception ex)
            {
                return new JudgeResult
                {
                    IsCorrect = false,
                    Result = "Error",
                    Output = "",
                    ErrorMessage = "AI 回傳格式錯誤，請人工檢查",
                    ExecutionTimeMs = 0,
                    Analysis = $"無法解析 AI 回應。原始回應：{jsonString}"
                };
            }
        }
    }

    public class JudgeResult
    {
        public bool IsCorrect { get; set; }             // 用於方便處理邏輯
        public string Result { get; set; }              // Correct / Wrong / Error / Timeout
        public string Output { get; set; }
        public string ErrorMessage { get; set; }
        public int ExecutionTimeMs { get; set; }
        public string Analysis { get; set; }
    }
}

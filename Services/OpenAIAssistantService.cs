using System.Collections.Generic;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using BattleCode.Models;

namespace BattleCode.Services
{
    public class OpenAIAssistantService
    {
        private readonly string _apiKey = ""; // 請填你自己的 Key
        private readonly string _assistantId = "";// 請填你自己的 Key
        // 呼叫 OpenAI API 產生多題程式題目
        public async Task<List<GeneratedProblem>> GenerateProblemsAsync(int count, string difficulty, string language)
        {
            // 系統提示，告訴 AI 需要什麼樣的題目格式與類別範圍
            var systemPrompt = $"請生成 {count} 題「{difficulty}」難度的 {language} 題目，請給中文題目，建議類別:基本語法、資料型態、條件判斷、迴圈、函式、串列、字典，請用以下格式列出每一題內容。\n\n" +
                               "題目格式如下：\n" +
                               "===\n" +
                               "Title:\nDescription:\nInput Format:\nOutput Format:\nSample Inputs:\nSample Outputs:\nTagName:\n===\n";

            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            // 準備要傳送給 OpenAI 的請求 JSON，包含系統與使用者訊息
            var request = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
            new { role = "system", content = "你是一位程式題目出題助理，請根據使用者指示生成多題題目。" },
            new { role = "user", content = systemPrompt }
        }
            };
            // 將請求物件序列化成 JSON 字串
            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            // 發送 POST 請求到 OpenAI Chat Completion API
            var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
            var responseString = await response.Content.ReadAsStringAsync();
            // 反序列化回傳結果
            dynamic result = JsonConvert.DeserializeObject(responseString);
            string reply = result.choices[0].message.content;
            // 呼叫解析函式把 AI 回傳的字串拆解成題目物件清單
            return ParseMultipleProblems(reply, difficulty);
        }
        // 將 AI 回傳的多題字串切割成多個題目並解析成物件列表
        private List<GeneratedProblem> ParseMultipleProblems(string response, string difficulty)
        {
            var problems = new List<GeneratedProblem>();
            // 用 === 做分割，因為系統提示裡用 === 分隔每題
            var problemChunks = response.Split(new[] { "===" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var chunk in problemChunks)
            {
                var trimmed = chunk.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;
                // 解析單題內容
                var parsed = ParseProblem(trimmed, difficulty);

                // ✅ 加入檢查：必須有標題與描述，否則略過避免不完整資料
                if (!string.IsNullOrWhiteSpace(parsed.Title) && !string.IsNullOrWhiteSpace(parsed.Description))
                {
                    problems.Add(parsed);
                }
            }

            return problems;
        }


        // 將單題字串解析成 GeneratedProblem 物件
        private GeneratedProblem ParseProblem(string response, string difficulty)
        {
            var problem = new GeneratedProblem();
            string[] lines = response.Split('\n');
            // 用來記錄目前正在解析哪個欄位，遇到沒有欄位標題的行會附加到目前欄位
            string currentField = "";

            foreach (var line in lines)
            {
                if (line.StartsWith("Title:"))
                {
                    currentField = "Title";
                    problem.Title = line.Replace("Title:", "").Trim();
                }
                else if (line.StartsWith("Description:"))
                {
                    currentField = "Description";
                    problem.Description = line.Replace("Description:", "").Trim();
                }
                else if (line.StartsWith("Input Format:"))
                {
                    currentField = "InputFormat";
                    problem.InputFormat = line.Replace("Input Format:", "").Trim();
                }
                else if (line.StartsWith("Output Format:"))
                {
                    currentField = "OutputFormat";
                    problem.OutputFormat = line.Replace("Output Format:", "").Trim();
                }
                else if (line.StartsWith("Sample Inputs:"))
                {
                    currentField = "SampleInputs";
                    problem.SampleInputs = line.Replace("Sample Inputs:", "").Trim();
                }
                else if (line.StartsWith("Sample Outputs:"))
                {
                    currentField = "SampleOutputs";
                    problem.SampleOutputs = line.Replace("Sample Outputs:", "").Trim();
                }
                else if (line.StartsWith("TagName:"))
                {
                    currentField = "TagName";
                    problem.TagName = line.Replace("TagName:", "").Trim();
                }
                else
                {
                    switch (currentField)
                    {
                        case "Description":
                            problem.Description += "\n" + line.Trim();
                            break;
                        case "InputFormat":
                            problem.InputFormat += "\n" + line.Trim();
                            break;
                        case "OutputFormat":
                            problem.OutputFormat += "\n" + line.Trim();
                            break;
                        case "SampleInputs":
                            problem.SampleInputs += "\n" + line.Trim();
                            break;
                        case "SampleOutputs":
                            problem.SampleOutputs += "\n" + line.Trim();
                            break;
                    }
                }
            }

            problem.Difficulty = difficulty;// 設定難度
            return problem;
        }
        // 通用的呼叫 OpenAI API 方法，傳入系統訊息與使用者訊息，回傳回應字串
        public async Task<string> CallOpenAI(string systemPrompt, string userPrompt)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var request = new
            {
                model = "gpt-4o-mini", // 或你自己的模型
                messages = new[]
                {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userPrompt }
        }
            };

            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
            var responseString = await response.Content.ReadAsStringAsync();

            dynamic result = JsonConvert.DeserializeObject(responseString);
            string reply = result.choices[0].message.content;

            return reply;
        }
        // 根據題目與使用者程式碼，產生 AI 給的錯誤提示或建議
        public async Task<string> GenerateHintFromCodeAsync(Problems prob, string code)
        {
            var systemPrompt = "你是一個程式助教，請根據以下題目與學生程式碼給他有幫助的修正建議，只能用繁體中文：\n";
            var userPrompt = $"題目：\n{prob.Title}\n{prob.Description}\n輸入格式：{prob.InputFormat}\n輸出格式：{prob.OutputFormat}\n範例輸入：{prob.SampleInputs}\n範例輸出：{prob.SampleOutputs}\n\n學生目前的程式碼：\n{code}\n\n請告訴學生哪裡寫錯、怎麼改才能對，並給一點方向提示，不要直接給答案。";
            var resp = await CallOpenAI(systemPrompt, userPrompt);
            return resp;
        }

        public async Task<string> GenerateHintFromProblemAsync(Problems prob)
        {
            var systemPrompt = "你是一個程式助教，請根據以下題目給學生一點解題方向，只能用繁體中文：\n";
            var userPrompt = $"題目：\n{prob.Title}\n{prob.Description}\n輸入格式：{prob.InputFormat}\n輸出格式：{prob.OutputFormat}\n範例輸入：{prob.SampleInputs}\n範例輸出：{prob.SampleOutputs}\n\n請給學生一點解題方向，不要直接給完整解答。";
            var resp = await CallOpenAI(systemPrompt, userPrompt);
            return resp;
        }
    }

    public class GeneratedProblem
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string InputFormat { get; set; }
        public string OutputFormat { get; set; }
        public string SampleInputs { get; set; }
        public string SampleOutputs { get; set; }
        public string Difficulty { get; set; }
        public string TagName { get; set; } // ✅ 新增 TagName 屬性
    }
}

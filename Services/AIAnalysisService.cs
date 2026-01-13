using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Linq;
using BattleCode.Models;

public class AIAnalysisService
{
    private readonly DatabaseEntities _db;// 資料庫上下文
    private readonly HttpClient _httpClient;// HttpClient 用來呼叫 OpenAI API

    // 建議改成從設定檔注入
    private readonly string _apiKey = "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"; // 建議放入 appsettings
    private readonly string _assistantId = "asst_I0K1C8wuaoEgeQOCf8wk1Xyv";

    public AIAnalysisService(DatabaseEntities db)
    {
        _db = db;
        _httpClient = new HttpClient();
        // 設定 Authorization Header，使用 Bearer Token
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }
    // 分析使用者的錯題，並用 AI 給出弱點分析與練習建議
    public async Task<string> AnalyzeUserMistakesAsync(int userId)
    {
        // 先查錯題統計
        var stats = _db.Submissions
            .Where(s => s.UserId == userId && s.Result != "Correct")// 只取錯誤的提交
            .Join(_db.Problems,
                  s => s.ProblemId,
                  p => p.ProblemId,
                  (s, p) => p.TagName)// 取得題目類別 Tag
            .GroupBy(tag => tag)// 分組計數
            .Select(g => new { Tag = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)// 錯題最多的排前面
            .ToList();
        // 如果沒有錯題，回傳提示訊息
        if (!stats.Any())
            return "你目前沒有錯題紀錄，請先完成幾個挑戰後再進行分析。";
        // 將錯題統計整理成文字格式
        var statText = string.Join("\n", stats.Select(s => $"- {s.Tag}：錯了 {s.Count} 題"));
        // 要給 AI 的提示詞（prompt），描述背景並請求建議
        var prompt = "我是一位正在練習程式設計的學生，以下是我各類型的錯題統計：\n\n"
               + statText + "\n\n"
               + "請你幫我分析我的弱點，並提供具體的練習建議（可以練哪些主題、加強哪種觀念、推薦什麼題型）。\n"
               + "請用條列式回答，並簡潔有力就好不用太過攏長。";


        // OpenAI Chat Completion API 呼叫
        var requestBody = new
        {
            model = "gpt-4o-mini",  // 或你要用的模型
            messages = new[]
            {
                new { role = "system", content = "你是專業程式設計教練" },
                new { role = "user", content = prompt }
            },
            temperature = 0.7,
            max_tokens = 600
        };
        // 把請求物件序列化成 JSON
        var jsonRequest = JsonConvert.SerializeObject(requestBody);
        var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
        // 發送 POST 請求到 OpenAI API
        var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
        if (!response.IsSuccessStatusCode)
        {
            return $"AI 服務錯誤：{response.StatusCode}";
        }

        var jsonResponse = await response.Content.ReadAsStringAsync();

        try
        {
            // 解析回傳的回答文字
            var chatResponse = JsonConvert.DeserializeObject<OpenAIChatResponse>(jsonResponse);
            var answer = chatResponse.choices.FirstOrDefault()?.message?.content ?? "AI 未回傳任何內容";
            return answer;
        }
        catch
        {
            return "AI 回傳格式錯誤，請稍後再試。";
        }
    }

    // 解析用的 class (只要取用到的部分)
    private class OpenAIChatResponse
    {
        public Choice[] choices { get; set; }
    }

    private class Choice
    {
        public Message message { get; set; }
    }

    private class Message
    {
        public string role { get; set; }
        public string content { get; set; }
    }
}

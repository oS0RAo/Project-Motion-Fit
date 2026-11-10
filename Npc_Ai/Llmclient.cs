using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class ChatMessage
{
    public string role;     // "user" หรือ "assistant"
    public string content;

    public ChatMessage(string role, string content)
    {
        this.role = role;
        this.content = content;
    }
}

public class LlmClient : MonoBehaviour
{
    [Header("Connection")]
    public string baseUrl = "https://generativelanguage.googleapis.com/v1beta";
    public string apiKey = "";
    [Tooltip("ใช้ Model ID เช่น gemini-2.5-flash-lite")]
    public string model = "gemini-2.5-flash-lite";

    [Header("Generation")]
    public int maxOutputTokens = 1024;
    [Range(0f, 2f)] public float temperature = 1f;
    [Tooltip("ใช้กับโมเดลตระกูล gemini-3 เท่านั้น")]
    public string thinkingLevel = "low";
    public bool sendThinkingConfig = true;

    [Header("Reliability")]
    public int timeoutSeconds = 60;
    [Range(1, 5)] public int maxAttempts = 3;
    public bool logRequests = true;

    public async Awaitable<string> SendStreamAsync(
        string systemPrompt,
        List<ChatMessage> history,
        Action<string> onChunk,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("ยังไม่ได้ใส่ API Key ใน LlmClient");

        if (history == null || history.Count == 0)
            throw new ArgumentException("History is empty.");

        string modelId = NormalizeModel(model);
        if (string.IsNullOrEmpty(modelId))
            throw new InvalidOperationException("ยังไม่ได้ใส่ Model ใน LlmClient");

        string body = BuildBody(systemPrompt, history, modelId);

        if (logRequests)
            Debug.Log($"[LlmClient] Model ID = {modelId}");

        int attempts = Mathf.Max(1, maxAttempts);

        for (int attempt = 1; ; attempt++)
        {
            AttemptResult result = await SendOnceAsync(
                modelId, body, onChunk, cancellationToken);

            if (result.ok)
                return result.text;

            bool canRetry =
                result.retryable &&
                !result.gotText &&
                attempt < attempts &&
                !cancellationToken.IsCancellationRequested;

            if (!canRetry)
                throw new Exception(result.error);

            float delay = Mathf.Pow(2f, attempt) + UnityEngine.Random.value;
            Debug.LogWarning(
                $"[LlmClient] ลองใหม่ครั้งที่ {attempt}/{attempts - 1} " +
                $"ใน {delay:0.0} วินาที\n{result.error}");

            await Awaitable.WaitForSecondsAsync(delay, cancellationToken);
        }
    }

    async Awaitable<AttemptResult> SendOnceAsync(
        string modelId,
        string body,
        Action<string> onChunk,
        CancellationToken ct)
    {
        string url =
            $"{baseUrl.TrimEnd('/')}/models/{Uri.EscapeDataString(modelId)}" +
            ":streamGenerateContent?alt=sse";

        var handler = new GeminiStreamHandler(onChunk);

        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        request.downloadHandler = handler;
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("x-goog-api-key", apiKey.Trim());
        request.timeout = Mathf.Max(5, timeoutSeconds);

        bool finished = false;

        try
        {
            UnityWebRequestAsyncOperation op = request.SendWebRequest();

            while (!op.isDone)
            {
                await Awaitable.NextFrameAsync(ct);

                if (handler.CallbackError != null)
                    break;
            }

            finished = op.isDone;
        }
        finally
        {
            if (!finished)
                request.Abort();
        }

        if (handler.CallbackError != null)
        {
            return AttemptResult.Fail(
                $"Error ตอนแสดงข้อความ: {handler.CallbackError.Message}",
                false, handler.GotText);
        }

        long code = request.responseCode;

        if (request.result != UnityWebRequest.Result.Success)
        {
            string detail = ExtractErrorMessage(handler.Raw);
            string message =
                $"Gemini HTTP {code}: {request.error}\n" +
                $"Model={modelId}\n" +
                (string.IsNullOrEmpty(detail) ? "" : $"Detail: {detail}");

            bool retryable =
                code == 0 || code == 429 || code == 500 ||
                code == 503 || code == 504;

            return AttemptResult.Fail(message, retryable, handler.GotText);
        }

        if (!string.IsNullOrEmpty(handler.ApiError))
        {
            string e = handler.ApiError;
            bool retryable =
                e.Contains("503") || e.Contains("UNAVAILABLE") ||
                e.Contains("429") || e.Contains("RESOURCE_EXHAUSTED") ||
                e.Contains("500") || e.Contains("INTERNAL");

            return AttemptResult.Fail(
                $"Gemini stream error: {e}\nModel={modelId}",
                retryable, handler.GotText);
        }

        string text = handler.Text.Trim();

        if (text.Length == 0)
        {
            return AttemptResult.Fail(
                "Gemini ไม่ได้ส่งข้อความกลับมา " +
                $"FinishReason={handler.FinishReason}, " +
                $"BlockReason={handler.BlockReason}, " +
                $"Model={modelId}, MaxOutputTokens={maxOutputTokens}",
                false, false);
        }

        return AttemptResult.Success(text);
    }

    // ---------- Request body ----------

    string BuildBody(string systemPrompt, List<ChatMessage> history, string modelId)
    {
        var sb = new StringBuilder(2048);
        sb.Append('{');

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            sb.Append("\"systemInstruction\":{\"parts\":[{\"text\":")
              .Append(JsonString(systemPrompt))
              .Append("}]},");
        }

        sb.Append("\"contents\":[");
        bool first = true;

        foreach (ChatMessage m in history)
        {
            if (m == null || string.IsNullOrWhiteSpace(m.content))
                continue;

            string role =
                m.role == "assistant" || m.role == "model" ? "model" : "user";

            if (!first) sb.Append(',');
            first = false;

            sb.Append("{\"role\":\"").Append(role)
              .Append("\",\"parts\":[{\"text\":")
              .Append(JsonString(m.content))
              .Append("}]}");
        }

        sb.Append("],\"generationConfig\":{");
        sb.Append("\"maxOutputTokens\":")
          .Append(Mathf.Max(16, maxOutputTokens).ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"temperature\":")
          .Append(Mathf.Clamp(temperature, 0f, 2f).ToString("0.###", CultureInfo.InvariantCulture));

        if (sendThinkingConfig)
            AppendThinkingConfig(sb, modelId);

        sb.Append("}}");
        return sb.ToString();
    }

    void AppendThinkingConfig(StringBuilder sb, string modelId)
    {
        if (modelId.StartsWith("gemini-2.5"))
        {
            // รุ่น 2.5 ใช้ thinkingBudget และรุ่น Pro ปิดการคิดไม่ได้
            if (!modelId.Contains("pro"))
                sb.Append(",\"thinkingConfig\":{\"thinkingBudget\":0}");
        }
        else if (modelId.StartsWith("gemini-3") &&
                 !string.IsNullOrWhiteSpace(thinkingLevel))
        {
            sb.Append(",\"thinkingConfig\":{\"thinkingLevel\":")
              .Append(JsonString(thinkingLevel.Trim().ToLowerInvariant()))
              .Append('}');
        }
        // รุ่นอื่นไม่ต้องส่ง thinkingConfig
    }

    // ---------- Helpers ----------

    static string NormalizeModel(string raw)
    {
        string m = (raw ?? "").Trim();

        if (m.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            m = m.Substring(7);

        m = m.ToLowerInvariant();
        m = Regex.Replace(m, @"\s+", "-");   // "gemini 2.5 flash lite" -> "gemini-2.5-flash-lite"
        return m;
    }

    static string JsonString(string s)
    {
        var sb = new StringBuilder(s.Length + 16);
        sb.Append('"');

        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    static string ExtractErrorMessage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        string json = raw.Trim();
        if (json.StartsWith("[")) json = json.TrimStart('[').TrimEnd(']').Trim();

        try
        {
            var wrapper = JsonUtility.FromJson<StreamResponse>(json);
            if (wrapper?.error != null && !string.IsNullOrEmpty(wrapper.error.message))
                return $"{wrapper.error.code} {wrapper.error.status}: {wrapper.error.message}";
        }
        catch { }

        return raw.Length > 1000 ? raw.Substring(0, 1000) : raw;
    }

    // ---------- Result ----------

    sealed class AttemptResult
    {
        public bool ok;
        public string text;
        public string error;
        public bool retryable;
        public bool gotText;

        public static AttemptResult Success(string text) =>
            new AttemptResult { ok = true, text = text };

        public static AttemptResult Fail(string error, bool retryable, bool gotText) =>
            new AttemptResult { error = error, retryable = retryable, gotText = gotText };
    }

    // ---------- Response JSON ----------

    [Serializable]
    class StreamResponse
    {
        public Candidate[] candidates;
        public PromptFeedback promptFeedback;
        public ApiError error;
    }
    [Serializable] class Candidate { public Content content; public string finishReason; }
    [Serializable] class Content { public Part[] parts; }
    [Serializable] class Part { public string text; public bool thought; }
    [Serializable] class PromptFeedback { public string blockReason; }
    [Serializable] class ApiError { public int code; public string message; public string status; }

    // ---------- SSE stream handler ----------

    sealed class GeminiStreamHandler : DownloadHandlerScript
    {
        readonly Decoder decoder = new UTF8Encoding(false).GetDecoder();
        readonly StringBuilder line = new();
        readonly StringBuilder eventData = new();
        readonly StringBuilder raw = new();
        readonly StringBuilder text = new();
        readonly Action<string> onChunk;
        char[] chars = new char[8192];

        public string Text => text.ToString();
        public string Raw => raw.ToString();
        public bool GotText => text.Length > 0;
        public string FinishReason { get; private set; }
        public string BlockReason { get; private set; }
        public string ApiError { get; private set; }
        public Exception CallbackError { get; private set; }

        public GeminiStreamHandler(Action<string> onChunk)
            : base(new byte[16 * 1024])
        {
            this.onChunk = onChunk;
        }

        protected override bool ReceiveData(byte[] data, int dataLength)
        {
            if (data == null || dataLength <= 0)
                return true;

            int needed = decoder.GetCharCount(data, 0, dataLength, false);
            if (chars.Length < needed) chars = new char[needed];

            int count = decoder.GetChars(data, 0, dataLength, chars, 0, false);
            Feed(chars, count);

            return CallbackError == null;
        }

        protected override void CompleteContent()
        {
            int count = decoder.GetChars(Array.Empty<byte>(), 0, 0, chars, 0, true);
            Feed(chars, count);

            if (line.Length > 0)
            {
                ProcessLine(line.ToString());
                line.Clear();
            }

            DispatchEvent();
        }

        void Feed(char[] buffer, int count)
        {
            for (int i = 0; i < count; i++)
            {
                char c = buffer[i];

                if (raw.Length < 16000)
                    raw.Append(c);

                if (c == '\n')
                {
                    ProcessLine(line.ToString());
                    line.Clear();
                }
                else if (c != '\r')
                {
                    line.Append(c);
                }
            }
        }

        void ProcessLine(string l)
        {
            if (l.Length == 0)
            {
                DispatchEvent();
                return;
            }

            if (!l.StartsWith("data:"))
                return;

            string d = l.Substring(5);
            if (d.StartsWith(" ")) d = d.Substring(1);

            if (eventData.Length > 0) eventData.Append('\n');
            eventData.Append(d);
        }

        void DispatchEvent()
        {
            if (eventData.Length == 0)
                return;

            string json = eventData.ToString();
            eventData.Clear();

            if (json == "[DONE]")
                return;

            StreamResponse r;
            try { r = JsonUtility.FromJson<StreamResponse>(json); }
            catch { return; }

            if (r == null)
                return;

            if (r.error != null && !string.IsNullOrEmpty(r.error.message))
            {
                ApiError = $"{r.error.code} {r.error.status}: {r.error.message}";
                return;
            }

            if (r.promptFeedback != null &&
                !string.IsNullOrEmpty(r.promptFeedback.blockReason))
            {
                BlockReason = r.promptFeedback.blockReason;
            }

            if (r.candidates == null)
                return;

            foreach (Candidate c in r.candidates)
            {
                if (c == null) continue;

                if (!string.IsNullOrEmpty(c.finishReason))
                    FinishReason = c.finishReason;

                if (c.content?.parts == null) continue;

                foreach (Part p in c.content.parts)
                {
                    // ไม่แสดงข้อความส่วนที่โมเดลใช้คิด
                    if (p == null || p.thought || string.IsNullOrEmpty(p.text))
                        continue;

                    text.Append(p.text);

                    try { onChunk?.Invoke(p.text); }
                    catch (Exception e) { CallbackError ??= e; }
                }
            }
        }
    }
}
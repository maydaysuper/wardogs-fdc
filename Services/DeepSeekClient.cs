using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WardogsNavigator.Services;

public sealed class DeepSeekClient
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://api.deepseek.com/") };

    public async Task<string> AskAsync(
        string apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("未设置 DeepSeek API Key。");

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(model) ? "deepseek-flash" : model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            max_tokens = 900,
            stream = false
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException("DeepSeek HTTP " + (int)resp.StatusCode + ": " + Trim(body, 300));

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        return content?.Trim() ?? "";
    }

    public async Task<T> AskJsonAsync<T>(
        string apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("未设置 DeepSeek API Key。");

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(model) ? "deepseek-flash" : model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            response_format = new { type = "json_object" },
            max_tokens = 1800,
            stream = false
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        req.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var resp = await _http.SendAsync(req, cancellationToken);
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                "DeepSeek HTTP " + (int)resp.StatusCode + ": " + Trim(body, 300));

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("DeepSeek JSON 输出为空。");

        return JsonSerializer.Deserialize<T>(
            content,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? throw new InvalidOperationException("DeepSeek JSON 无法解析。");
    }

    public async Task<T> AskJsonWithImagesAsync<T>(
        string apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<byte[]> pngImages,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("未设置 DeepSeek API Key。");

        if (pngImages.Count == 0)
            throw new InvalidOperationException("视觉分析至少需要一张图片。");

        var content = new List<object>
        {
            new { type = "text", text = userPrompt }
        };

        foreach (var bytes in pngImages)
        {
            var dataUrl =
                "data:image/png;base64," +
                Convert.ToBase64String(bytes);

            content.Add(new
            {
                type = "image_url",
                image_url = new
                {
                    url = dataUrl,
                    detail = "high"
                }
            });
        }

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(model)
                ? "deepseek-flash"
                : model,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = systemPrompt
                },
                new
                {
                    role = "user",
                    content
                }
            },
            response_format = new { type = "json_object" },
            max_tokens = 2200,
            stream = false
        };

        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            "chat/completions");

        req.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                apiKey.Trim());

        req.Content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var resp = await _http.SendAsync(
            req,
            cancellationToken);

        var body = await resp.Content.ReadAsStringAsync(
            cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                "DeepSeek HTTP " +
                (int)resp.StatusCode +
                ": " +
                Trim(body, 300));

        using var doc = JsonDocument.Parse(body);
        var responseText = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(responseText))
            throw new InvalidOperationException(
                "DeepSeek 视觉 JSON 输出为空。");

        return JsonSerializer.Deserialize<T>(
            responseText,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? throw new InvalidOperationException(
                "DeepSeek 视觉 JSON 无法解析。");
    }

    public Task<string> TestAsync(string apiKey, string model, CancellationToken cancellationToken = default) =>
        AskAsync(apiKey, model, "只进行连接测试。", "只回复：连接成功", cancellationToken);

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}

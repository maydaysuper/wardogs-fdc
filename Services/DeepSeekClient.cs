using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WardogsNavigator.Services;

public sealed class DeepSeekClient
{
    private static readonly HttpClient Http = CreateClient();

    public async Task<string> AskAsync(
        string apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            model = NormalizeModel(model),
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            max_tokens = 900,
            stream = false
        };

        var content = await SendAndReadContentAsync(
            apiKey,
            payload,
            cancellationToken);

        return content.Trim();
    }

    public async Task<T> AskJsonAsync<T>(
        string apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            model = NormalizeModel(model),
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            response_format = new { type = "json_object" },
            max_tokens = 1800,
            stream = false
        };

        return await SendJsonAsync<T>(
            apiKey,
            payload,
            cancellationToken);
    }

    public Task<T> AskJsonWithImagesAsync<T>(
        string apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<byte[]> pngImages,
        CancellationToken cancellationToken = default) =>
        AskJsonWithImagesAsync<T>(
            apiKey,
            model,
            systemPrompt,
            userPrompt,
            pngImages,
            "high",
            cancellationToken);

    public async Task<T> AskJsonWithImagesAsync<T>(
        string apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<byte[]> pngImages,
        string imageDetail,
        CancellationToken cancellationToken = default)
    {
        if (pngImages.Count == 0)
            throw new InvalidOperationException("视觉分析至少需要一张图片。");

        var content = new List<object>
        {
            new { type = "text", text = userPrompt }
        };

        foreach (var bytes in pngImages)
        {
            content.Add(new
            {
                type = "image_url",
                image_url = new
                {
                    url = "data:image/png;base64," + Convert.ToBase64String(bytes),
                    detail = imageDetail.Equals("low", StringComparison.OrdinalIgnoreCase)
                        ? "low"
                        : "high"
                }
            });
        }

        var payload = new
        {
            model = NormalizeModel(model),
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content }
            },
            response_format = new { type = "json_object" },
            max_tokens = 2000,
            stream = false
        };

        return await SendJsonAsync<T>(
            apiKey,
            payload,
            cancellationToken);
    }

    public Task<string> TestAsync(
        string apiKey,
        string model,
        CancellationToken cancellationToken = default) =>
        AskAsync(
            apiKey,
            model,
            "只进行连接测试。",
            "只回复：连接成功",
            cancellationToken);

    private static async Task<T> SendJsonAsync<T>(
        string apiKey,
        object payload,
        CancellationToken cancellationToken)
    {
        Exception? last = null;

        // JSON mode can occasionally return empty content. One deterministic
        // retry is cheaper and safer than forcing the caller to restart.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var content = await SendAndReadContentAsync(
                    apiKey,
                    payload,
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(content))
                    throw new InvalidOperationException("DeepSeek JSON 输出为空。");

                return JsonSerializer.Deserialize<T>(
                    content,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    })
                    ?? throw new InvalidOperationException(
                        "DeepSeek JSON 无法解析。");
            }
            catch (Exception ex) when (
                attempt == 0 &&
                ex is not OperationCanceledException)
            {
                last = ex;
                await Task.Delay(350, cancellationToken);
            }
        }

        throw last ?? new InvalidOperationException("DeepSeek JSON 请求失败。");
    }

    private static async Task<string> SendAndReadContentAsync(
        string apiKey,
        object payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("未设置 DeepSeek API Key。");

        Exception? last = null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
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

            try
            {
                using var resp = await Http.SendAsync(
                    req,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                var body = await resp.Content.ReadAsStringAsync(
                    cancellationToken);

                if (resp.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(body);

                    return doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString()
                        ?.Trim()
                        ?? "";
                }

                var retryable =
                    resp.StatusCode == HttpStatusCode.TooManyRequests ||
                    (int)resp.StatusCode >= 500;

                var error = new InvalidOperationException(
                    "DeepSeek HTTP " +
                    (int)resp.StatusCode +
                    ": " +
                    Trim(body, 300));

                if (!retryable || attempt > 0)
                    throw error;

                last = error;
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException &&
                attempt == 0)
            {
                last = ex;
            }

            await Task.Delay(550, cancellationToken);
        }

        throw last ?? new InvalidOperationException("DeepSeek 请求失败。");
    }

    private static string NormalizeModel(string? model)
    {
        var value = string.IsNullOrWhiteSpace(model)
            ? "deepseek-flash"
            : model.Trim();

        return value switch
        {
            "deepseek-v4-flash" => "deepseek-flash",
            "deepseek-v4-flash-vision-exp" => "deepseek-flash",
            _ => value
        };
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://api.deepseek.com/"),
            Timeout = TimeSpan.FromSeconds(45)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "WardogsNavigator/0.10");

        return client;
    }

    private static string Trim(string s, int max) =>
        s.Length <= max
            ? s
            : s[..max] + "…";
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SklandAutoSign;

internal sealed record SignResult(bool Success, string Message);

internal sealed class SklandClient : IDisposable
{
    private const string AppCode = "4ca99fa6b56cc2ba";
    private const string Platform = "3";
    private const string VersionName = "1.0.0";
    private const string GrantUrl = "https://as.hypergryph.com/user/oauth2/v2/grant";
    private const string CredUrl = "https://zonai.skland.com/api/v1/user/auth/generate_cred_by_code";
    private const string BindingUrl = "https://zonai.skland.com/api/v1/game/player/binding";
    private const string ArknightsAttendanceUrl = "https://zonai.skland.com/api/v1/game/attendance";
    private const string EndfieldAttendanceUrl = "https://zonai.skland.com/api/v1/game/endfield/attendance";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public SklandClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Skland/1.0.1 (com.hypergryph.skland; build:100001014; Android 31; ) Okhttp/4.11.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9");
    }

    public async Task<IReadOnlyList<SignResult>> SignSelectedAsync(
        string pastedToken,
        bool signArknights,
        bool signEndfield,
        CancellationToken cancellationToken = default)
    {
        if (!signArknights && !signEndfield)
            throw new InvalidOperationException("请至少选择一款游戏。");

        string token = NormalizeToken(pastedToken);
        (string cred, string signToken) = await LoginAsync(token, cancellationToken);
        BindingInfo bindings = await GetBindingsAsync(cred, signToken, cancellationToken);
        var results = new List<SignResult>();

        if (signArknights)
        {
            if (bindings.Arknights.Count == 0)
                results.Add(new SignResult(false, "[明日方舟] 未找到已绑定角色。"));
            foreach (ArknightsRole role in bindings.Arknights)
            {
                try
                {
                    results.Add(await SignArknightsAsync(cred, signToken, role, cancellationToken));
                }
                catch (Exception ex)
                {
                    results.Add(new SignResult(false, $"[明日方舟] {role.Nickname}（{role.ChannelName}）：{ex.Message}"));
                }
            }
        }

        if (signEndfield)
        {
            if (bindings.Endfield.Count == 0)
                results.Add(new SignResult(false, "[终末地] 未找到已绑定角色。"));
            foreach (EndfieldRole role in bindings.Endfield)
            {
                try
                {
                    results.Add(await SignEndfieldAsync(cred, signToken, role, cancellationToken));
                }
                catch (Exception ex)
                {
                    results.Add(new SignResult(false, $"[终末地] {role.Nickname}（{role.ChannelName}）：{ex.Message}"));
                }
            }
        }

        return results;
    }

    internal static string NormalizeToken(string input)
    {
        string value = input.Trim();
        if (value.StartsWith('{'))
        {
            using JsonDocument document = JsonDocument.Parse(value);
            if (document.RootElement.TryGetProperty("data", out JsonElement data) &&
                data.TryGetProperty("content", out JsonElement content))
                value = content.GetString()?.Trim() ?? "";
        }
        value = value.Trim('"');
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Token 为空。");
        return value;
    }

    private async Task<(string Cred, string SignToken)> LoginAsync(string token, CancellationToken cancellationToken)
    {
        string grantBody = JsonSerializer.Serialize(new { appCode = AppCode, token, type = 0 });
        using HttpResponseMessage grantResponse = await SendJsonAsync(HttpMethod.Post, GrantUrl, grantBody, null, cancellationToken);
        using JsonDocument grantJson = await ReadJsonAsync(grantResponse, "获取授权码", cancellationToken);
        JsonElement grantRoot = grantJson.RootElement;
        if (GetInt(grantRoot, "status") != 0)
            throw new InvalidOperationException($"获取授权码失败：{GetMessage(grantRoot)}");
        string grantCode = grantRoot.GetProperty("data").GetProperty("code").GetString()
            ?? throw new InvalidOperationException("授权响应中缺少 code。");

        string credBody = JsonSerializer.Serialize(new { code = grantCode, kind = 1 });
        using HttpResponseMessage credResponse = await SendJsonAsync(HttpMethod.Post, CredUrl, credBody, null, cancellationToken);
        using JsonDocument credJson = await ReadJsonAsync(credResponse, "获取森空岛凭证", cancellationToken);
        JsonElement credRoot = credJson.RootElement;
        if (GetInt(credRoot, "code") != 0)
            throw new InvalidOperationException($"获取森空岛凭证失败：{GetMessage(credRoot)}");
        JsonElement credData = credRoot.GetProperty("data");
        string cred = credData.GetProperty("cred").GetString()
            ?? throw new InvalidOperationException("凭证响应中缺少 cred。");
        string signToken = credData.GetProperty("token").GetString()
            ?? throw new InvalidOperationException("凭证响应中缺少签名 token。");
        return (cred, signToken);
    }

    private async Task<BindingInfo> GetBindingsAsync(string cred, string signToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BindingUrl);
        AddSignedHeaders(request, cred, signToken, new Uri(BindingUrl).AbsolutePath, "");
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        using JsonDocument json = await ReadJsonAsync(response, "获取绑定角色", cancellationToken);
        JsonElement root = json.RootElement;
        if (GetInt(root, "code") != 0)
            throw new InvalidOperationException($"获取绑定角色失败：{GetMessage(root)}");

        var arknights = new List<ArknightsRole>();
        var endfield = new List<EndfieldRole>();
        foreach (JsonElement app in root.GetProperty("data").GetProperty("list").EnumerateArray())
        {
            string appCode = GetOptionalString(app, "appCode", "");
            if (!app.TryGetProperty("bindingList", out JsonElement bindingList) || bindingList.ValueKind != JsonValueKind.Array)
                continue;

            foreach (JsonElement binding in bindingList.EnumerateArray())
            {
                if (string.Equals(appCode, "arknights", StringComparison.OrdinalIgnoreCase))
                {
                    string uid = ReadOptionalStringOrNumber(binding, "uid");
                    string gameId = ReadOptionalStringOrNumber(binding, "channelMasterId");
                    if (!string.IsNullOrWhiteSpace(uid) && !string.IsNullOrWhiteSpace(gameId))
                    {
                        arknights.Add(new ArknightsRole(
                            uid,
                            gameId,
                            GetOptionalString(binding, "nickName", "未知角色"),
                            GetOptionalString(binding, "channelName", "未知渠道")));
                    }
                }
                else if (string.Equals(appCode, "endfield", StringComparison.OrdinalIgnoreCase))
                {
                    string uid = ReadOptionalStringOrNumber(binding, "uid");
                    if (!binding.TryGetProperty("defaultRole", out JsonElement role) || role.ValueKind != JsonValueKind.Object)
                    {
                        if (!binding.TryGetProperty("roles", out JsonElement roles) || roles.ValueKind != JsonValueKind.Array || roles.GetArrayLength() == 0)
                            continue;
                        role = roles[0];
                    }

                    string roleId = ReadOptionalStringOrNumber(role, "roleId");
                    string serverId = ReadOptionalStringOrNumber(role, "serverId");
                    if (!string.IsNullOrWhiteSpace(uid) && !string.IsNullOrWhiteSpace(roleId) && !string.IsNullOrWhiteSpace(serverId))
                    {
                        endfield.Add(new EndfieldRole(
                            uid,
                            roleId,
                            serverId,
                            GetOptionalString(role, "nickname", "未知角色"),
                            GetOptionalString(binding, "channelName", GetOptionalString(role, "serverName", "未知服务器"))));
                    }
                }
            }
        }
        return new BindingInfo(arknights, endfield);
    }

    private async Task<SignResult> SignArknightsAsync(
        string cred, string signToken, ArknightsRole role, CancellationToken cancellationToken)
    {
        string body = JsonSerializer.Serialize(new { uid = role.Uid, gameId = role.GameId });
        using HttpResponseMessage response = await SendSignedJsonAsync(
            ArknightsAttendanceUrl, body, cred, signToken, cancellationToken);
        using JsonDocument json = await ReadJsonAsync(response, "明日方舟签到", cancellationToken);
        JsonElement root = json.RootElement;
        string label = $"[明日方舟] {role.Nickname}（{role.ChannelName}）";
        return ParseAttendanceResult(root, label, FormatArknightsRewards);
    }

    private async Task<SignResult> SignEndfieldAsync(
        string cred, string signToken, EndfieldRole role, CancellationToken cancellationToken)
    {
        string body = JsonSerializer.Serialize(new
        {
            uid = role.Uid,
            gameId = 3,
            roleId = role.RoleId,
            serverId = role.ServerId
        });
        using HttpResponseMessage response = await SendSignedJsonAsync(
            EndfieldAttendanceUrl, body, cred, signToken, cancellationToken);
        using JsonDocument json = await ReadJsonAsync(response, "终末地签到", cancellationToken);
        JsonElement root = json.RootElement;
        string label = $"[终末地] {role.Nickname}（{role.ChannelName}）";
        return ParseAttendanceResult(root, label, FormatEndfieldRewards);
    }

    private static SignResult ParseAttendanceResult(
        JsonElement root,
        string label,
        Func<JsonElement, string> rewardFormatter)
    {
        int code = GetInt(root, "code");
        string message = GetMessage(root);
        if (code == 0)
        {
            string rewards = rewardFormatter(root);
            return new SignResult(true, string.IsNullOrWhiteSpace(rewards)
                ? $"{label}：签到成功。"
                : $"{label}：签到成功，获得 {rewards}。");
        }

        if (IsAlreadySigned(code, message))
            return new SignResult(true, $"{label}：今日已经签到。服务端信息：{message}");

        return new SignResult(false, $"{label}：签到失败（代码 {code}）：{message}");
    }

    private static bool IsAlreadySigned(int code, string message) =>
        code == 10001 ||
        message.Contains("重复签到", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("已经签到", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("已签到", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("again", StringComparison.OrdinalIgnoreCase);

    private async Task<HttpResponseMessage> SendSignedJsonAsync(
        string url, string body, string cred, string signToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        AddSignedHeaders(request, cred, signToken, new Uri(url).AbsolutePath, body);
        return await _http.SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendJsonAsync(
        HttpMethod method, string url, string body, Action<HttpRequestMessage>? configure, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        configure?.Invoke(request);
        return await _http.SendAsync(request, cancellationToken);
    }

    private static void AddSignedHeaders(HttpRequestMessage request, string cred, string signToken, string path, string body)
    {
        string timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        string signHeaderJson = $"{{\"platform\":\"{Platform}\",\"timestamp\":\"{timestamp}\",\"dId\":\"\",\"vName\":\"{VersionName}\"}}";
        string source = path + body + timestamp + signHeaderJson;
        byte[] hmac;
        using (var hmacSha256 = new HMACSHA256(Encoding.UTF8.GetBytes(signToken)))
            hmac = hmacSha256.ComputeHash(Encoding.UTF8.GetBytes(source));
        string hmacHex = Convert.ToHexString(hmac).ToLowerInvariant();
        string sign = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(hmacHex))).ToLowerInvariant();

        request.Headers.TryAddWithoutValidation("cred", cred);
        request.Headers.TryAddWithoutValidation("platform", Platform);
        request.Headers.TryAddWithoutValidation("timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("dId", "");
        request.Headers.TryAddWithoutValidation("vName", VersionName);
        request.Headers.TryAddWithoutValidation("sign", sign);
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            string status = $"HTTP {(int)response.StatusCode}";
            string preview = body.Length > 160 ? body[..160] + "…" : body;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(preview)
                ? $"{operation}请求失败：{status}，响应为空。"
                : $"{operation}请求失败：{status}，响应：{preview}");
        }
    }

    private static int GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
            return -1;
        return value.ValueKind == JsonValueKind.Number ? value.GetInt32() :
            int.TryParse(value.GetString(), out int number) ? number : -1;
    }

    private static string GetMessage(JsonElement element)
    {
        foreach (string property in new[] { "message", "msg" })
            if (element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "未知错误";
        return "未知错误";
    }

    private static string ReadOptionalStringOrNumber(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value))
            return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
    }

    private static string GetOptionalString(JsonElement element, string name, string fallback) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static string FormatArknightsRewards(JsonElement root)
    {
        if (!root.TryGetProperty("data", out JsonElement data) ||
            !data.TryGetProperty("awards", out JsonElement awards) || awards.ValueKind != JsonValueKind.Array)
            return "";

        var parts = new List<string>();
        foreach (JsonElement award in awards.EnumerateArray())
        {
            if (!award.TryGetProperty("resource", out JsonElement resource))
                continue;
            string name = GetOptionalString(resource, "name", "未知奖励");
            string count = award.TryGetProperty("count", out JsonElement countElement)
                ? countElement.GetRawText().Trim('"')
                : "1";
            parts.Add($"{name}×{count}");
        }
        return string.Join("、", parts);
    }

    private static string FormatEndfieldRewards(JsonElement root)
    {
        if (!root.TryGetProperty("data", out JsonElement data) ||
            !data.TryGetProperty("awardIds", out JsonElement awards) || awards.ValueKind != JsonValueKind.Array ||
            !data.TryGetProperty("resourceInfoMap", out JsonElement resources) || resources.ValueKind != JsonValueKind.Object)
            return "";

        var parts = new List<string>();
        foreach (JsonElement award in awards.EnumerateArray())
        {
            string id = ReadOptionalStringOrNumber(award, "id");
            if (string.IsNullOrEmpty(id) || !resources.TryGetProperty(id, out JsonElement resource))
                continue;
            string name = GetOptionalString(resource, "name", id);
            string count = resource.TryGetProperty("count", out JsonElement countElement)
                ? countElement.GetRawText().Trim('"')
                : "1";
            parts.Add($"{name}×{count}");
        }
        return string.Join("、", parts);
    }

    public void Dispose() => _http.Dispose();

    private sealed record ArknightsRole(string Uid, string GameId, string Nickname, string ChannelName);
    private sealed record EndfieldRole(string Uid, string RoleId, string ServerId, string Nickname, string ChannelName);
    private sealed record BindingInfo(List<ArknightsRole> Arknights, List<EndfieldRole> Endfield);
}

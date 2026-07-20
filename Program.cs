namespace SklandAutoSign;

internal static class Program
{
    [STAThread]
    private static async Task Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Any(x => string.Equals(x, "--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = await SelfTestRunner.RunAsync();
            return;
        }

        if (args.Any(x => string.Equals(x, "--run", StringComparison.OrdinalIgnoreCase)))
        {
            await HeadlessRunner.RunAsync();
            return;
        }

        Application.Run(new MainForm());
    }
}

internal static class SelfTestRunner
{
    public static async Task<int> RunAsync()
    {
        var results = new List<string>();
        try
        {
            const string secret = "diagnostic-secret";
            string encrypted = SecretProtector.Protect(secret);
            if (encrypted == secret || SecretProtector.Unprotect(encrypted) != secret)
                throw new InvalidOperationException("DPAPI 加解密往返测试失败。");
            results.Add("PASS DPAPI 加解密");

            string normalized = SklandClient.NormalizeToken("{\"code\":0,\"data\":{\"content\":\"test-token\"}}");
            if (normalized != "test-token")
                throw new InvalidOperationException("Token JSON 解析测试失败。");
            results.Add("PASS Token JSON 解析");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using HttpResponseMessage response = await http.GetAsync("https://zonai.skland.com/api/v1/game/player/binding");
            string responseText = await response.Content.ReadAsStringAsync();
            if (!responseText.Contains("10002", StringComparison.Ordinal))
                throw new InvalidOperationException("森空岛端点连通性测试未得到预期响应。");
            results.Add("PASS 森空岛端点连通性");

            results.Add("SELF-TEST PASSED");
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test.log"), results, new System.Text.UTF8Encoding(false));
            return 0;
        }
        catch (Exception ex)
        {
            results.Add($"SELF-TEST FAILED: {ex.Message}");
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test.log"), results, new System.Text.UTF8Encoding(false));
            return 1;
        }
    }
}

internal static class HeadlessRunner
{
    public static async Task RunAsync()
    {
        try
        {
            AppSettings settings = SettingsStore.Load();
            if (string.IsNullOrWhiteSpace(settings.EncryptedToken))
            {
                LogWriter.Write("定时签到跳过：尚未保存账号 Token。");
                return;
            }

            string token = SecretProtector.Unprotect(settings.EncryptedToken);
            using var client = new SklandClient();
            IReadOnlyList<SignResult> results = await client.SignSelectedAsync(
                token, settings.EnableArknights, settings.EnableEndfield);
            foreach (SignResult result in results)
                LogWriter.Write(result.Message);
        }
        catch (Exception ex)
        {
            LogWriter.Write($"定时签到失败：{ex.Message}");
        }
    }
}

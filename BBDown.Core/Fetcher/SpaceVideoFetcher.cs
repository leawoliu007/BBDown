using BBDown.Core.Entity;
using System.Text.Json;
using static BBDown.Core.Util.HTTPUtil;
using static BBDown.Core.Logger;
using System.Runtime.InteropServices;

namespace BBDown.Core.Fetcher;

public class SpaceVideoFetcher : IFetcher
{
    public async Task<VInfo> FetchAsync(string id)
    {
        id = id[4..];
        // using the live API can bypass w_rid
        string userInfoApi = $"https://api.live.bilibili.com/live_user/v1/Master/info?uid={id}";
        string userName = GetValidFileName(JsonDocument.Parse(await GetWebSourceAsync(userInfoApi)).RootElement.GetProperty("data").GetProperty("info").GetProperty("uname").ToString(), ".", true);
        List<string> urls = new();
        int pageSize = 50;
        int pageNumber = 1;
        var api = Parser.WbiSign($"mid={id}&order=pubdate&pn={pageNumber}&ps={pageSize}&tid=0&wts={DateTimeOffset.Now.ToUnixTimeSeconds().ToString()}");
        api = $"https://api.bilibili.com/x/space/wbi/arc/search?{api}";
        string json = await GetWebSourceAsync(api);
        var infoJson = JsonDocument.Parse(json);
        var pages = infoJson.RootElement.GetProperty("data").GetProperty("list").GetProperty("vlist").EnumerateArray();
        foreach (var page in pages)
        {
            urls.Add($"https://www.bilibili.com/video/av{page.GetProperty("aid")}");
        }
        int totalCount = infoJson.RootElement.GetProperty("data").GetProperty("page").GetProperty("count").GetInt32();
        int totalPage = (int)Math.Ceiling((double)totalCount / pageSize);
        while (pageNumber < totalPage)
        {
            pageNumber++;
            urls.AddRange(await GetVideosByPageAsync(pageNumber, pageSize, id));
        }
// Save URLs to a file (optional, for reference)
        string filePath = Path.Combine(Directory.GetCurrentDirectory(), $"{userName}的投稿视频.txt");
        await File.WriteAllTextAsync(filePath, string.Join(Environment.NewLine, urls));
        Log($"已获取 {urls.Count} 个视频地址，保存至 {filePath}，开始批量下载...");

        // Execute batch download with OS-specific logic
        await DownloadVideosAsync(urls);

        Log("批量下载完成！");
        throw new Exception("暂不支持该功能");
    }

    static async Task<List<string>> GetVideosByPageAsync(int pageNumber, int pageSize, string mid)
    {
        List<string> urls = new();
        var api = Parser.WbiSign($"mid={mid}&order=pubdate&pn={pageNumber}&ps={pageSize}&tid=0&wts={DateTimeOffset.Now.ToUnixTimeSeconds().ToString()}");
        api = $"https://api.bilibili.com/x/space/wbi/arc/search?{api}";
        string json = await GetWebSourceAsync(api);
        var infoJson = JsonDocument.Parse(json);
        var pages = infoJson.RootElement.GetProperty("data").GetProperty("list").GetProperty("vlist").EnumerateArray();
        foreach (var page in pages)
        {
            urls.Add($"https://www.bilibili.com/video/av{page.GetProperty("aid")}");
        }
        return urls;
    }

    private static string GetValidFileName(string input, string re = ".", bool filterSlash = false)
    {
        string title = input;
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            title = title.Replace(invalidChar.ToString(), re);
        }
        if (filterSlash)
        {
            title = title.Replace("/", re);
            title = title.Replace("\\", re);
        }
        return title;
    }

    private async Task DownloadVideosAsync(List<string> urls)
    {
        string executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "BBDown.exe" : "BBDown";
        string argumentsPrefix = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "" : "./"; // Use ./ for Linux if in current dir

        // Check if the executable exists in the current directory or PATH
        string executablePath = File.Exists(Path.Combine(Directory.GetCurrentDirectory(), executableName))
            ? Path.Combine(Directory.GetCurrentDirectory(), executableName)
            : executableName; // Fallback to PATH

        foreach (var url in urls)
        {
            try
            {
                Log($"正在下载: {url}");
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = executablePath,
                        Arguments = $"\"{url}\"", // Quote the URL to handle spaces or special characters
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    Log($"下载成功: {url}");
                    if (!string.IsNullOrEmpty(output)) LogDebug(output);
                }
                else
                {
                    LogError($"下载失败: {url}，错误信息: {error}");
                }
            }
            catch (Exception ex)
            {
                LogError($"下载 {url} 时发生异常: {ex.Message}");
                if (ex is FileNotFoundException)
                {
                    LogError($"未找到 {executableName}，请确保它在当前目录或系统 PATH 中。");
                    break;
                }
            }
        }
    }
}

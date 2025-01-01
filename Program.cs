﻿using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

// Authentication methods for ZTE Routers and SMS retrieving
// =======================================
// ZTE routers implement three distinct methods to encode passwords for authentication purposes.
// The more sophisticated methods (1 and 2) probably act as a security mechanism to compensate for
// the use of HTTP instead of HTTPS in the router's web interface.
// The specific authentication method is determined by the `WEB_ATTR_IF_SUPPORT_SHA256` value,
// which is hardcoded into the router's firmware.

// Method 1: SHA256 with Base64 (WEB_ATTR_IF_SUPPORT_SHA256 = 1)
// ------------------------------------------------------------
// - Takes the plaintext password.
// - Encodes it in Base64.
// - Computes the SHA256 hash of the Base64 string.
// - Status: Not tested in production.
// - Use case: Older router models.
// - Note: Offers moderate security but is less efficient than Method 2.

// Method 2: Double SHA256 with LD Value (WEB_ATTR_IF_SUPPORT_SHA256 = 2)
// ----------------------------------------------------------------------
// - Takes the plaintext password.
// - Computes the first SHA256 hash of the password.
// - Concatenates the resulting hash with the LD value (a server-generated timestamp-based challenge).
// - Computes a second SHA256 hash of the concatenated string.
// - Status: Tested and confirmed working on the MC888 router model.
// - Use case: Current generation routers.
// - Note: This is the most secure method implemented by ZTE routers.

// Method 3: Simple Base64 (WEB_ATTR_IF_SUPPORT_SHA256 = any other value)
// ----------------------------------------------------------------------
// - Takes the plaintext password.
// - Encodes it using simple Base64 encoding.
// - Status: Not tested in production.
// - Use case: Legacy router models.
// - Note: This is the least secure method and is only used for backward compatibility.

// Important Notes:
// ----------------
// 1. The `WEB_ATTR_IF_SUPPORT_SHA256` value is embedded in the router's firmware and cannot be modified without a firmware update.
// 2. The information about these methods was obtained through reverse engineering of the web interface, specifically its JavaScript code.

// Class Description
// =================
// This program implements a client for ZTE router authentication and SMS management.
// It handles login procedures, SMS retrieval, and tracks messages' status using the
// Windows Registry for detecting changes in the SMS list.

class Program
{
    private const string BaseUrlTemplate = "http://{0}/goform/";
    private const string RegistryKeyPath = @"SOFTWARE\ZTEStatus";
    private const string RegistryValueName = "SmsListHash";
    private static string baseUrl;

    static async Task Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: GetSMSZTE <modem_ip> <password>");
            return;
        }

        string ip = args[0];
        string password = args[1];
        baseUrl = string.Format(BaseUrlTemplate, ip);

        try
        {
            using HttpClientHandler handler = new HttpClientHandler { UseCookies = true };
            using HttpClient client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("Referer", $"http://{ip}/");

            string ldValue = await GetLDValue(client);
            if (string.IsNullOrEmpty(ldValue))
            {
                Console.WriteLine("LD not found.");
                return;
            }

            string finalHash = GenerateFinalHash(password, ldValue);
            bool loginSuccess = await PerformLogin(client, finalHash);

            if (!loginSuccess) return;

            await GetSmsCapacity(client);
            await ProcessSmsList(client);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves the LD value from the router. The LD value is a timestamp-based challenge
    /// generated by the server and is used during the authentication process.
    /// </summary>
    /// <param name="client">An instance of HttpClient for sending HTTP requests.</param>
    /// <returns>The LD value as a string, or null if retrieval fails.</returns>
    static async Task<string> GetLDValue(HttpClient client)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string url = $"{baseUrl}goform_get_cmd_process?isTest=false&cmd=LD&_={timestamp}";

        HttpResponseMessage response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        string responseBody = await response.Content.ReadAsStringAsync();

        using JsonDocument doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("LD").GetString();
    }

    /// <summary>
    /// Generates the final hash for authentication based on the password, LD value,
    /// and the selected WEB_ATTR_IF_SUPPORT_SHA256 method.
    /// </summary>
    /// <param name="password">The router's plaintext password.</param>
    /// <param name="ldValue">The LD value obtained from the router.</param>
    /// <param name="webAttrIfSupportSha256">
    /// Determines the hashing method:
    /// 1 = SHA256 of the password encoded in Base64.
    /// 2 = Double SHA256 of the password and LD value.
    /// Any other value = Simple Base64 encoding of the password.
    /// </param>
    /// <returns>The computed hash string.</returns>
    static string GenerateFinalHash(string password, string ldValue, int webAttrIfSupportSha256 = 2)
    {
        switch (webAttrIfSupportSha256)
        {
            case 1: // Not tested
                return ComputeSHA256(ConvertBase64(password));
            case 2:  // Tested on MC888
                string hashedPassword = ComputeSHA256(password);
                return ComputeSHA256(hashedPassword + ldValue);
            default: // Not tested
                return ConvertBase64(password);
        }
    }

    /// <summary>
    /// Logs into the router using the provided final hash.
    /// </summary>
    /// <param name="client">An instance of HttpClient for sending HTTP requests.</param>
    /// <param name="finalHash">The hash to be used for authentication.</param>
    /// <returns>True if login is successful, otherwise false.</returns>
    static async Task<bool> PerformLogin(HttpClient client, string finalHash)
    {
        string postData = $"isTest=false&goformId=LOGIN&password={finalHash}";
        HttpContent content = new StringContent(postData, Encoding.UTF8, "application/x-www-form-urlencoded");

        HttpResponseMessage response = await client.PostAsync($"{baseUrl}goform_set_cmd_process", content);
        response.EnsureSuccessStatusCode();
        string responseBody = await response.Content.ReadAsStringAsync();

        using JsonDocument doc = JsonDocument.Parse(responseBody);
        string result = doc.RootElement.GetProperty("result").GetString();

        if (result == "0") return true;

        Console.WriteLine(result == "1" ? "Account is locked." : "Login unsuccessful.");
        return false;
    }

    /// <summary>
    /// Retrieves information about SMS capacity from the router.
    /// </summary>
    /// <param name="client">An instance of HttpClient for sending HTTP requests.</param>
    static async Task GetSmsCapacity(HttpClient client)
    {
        string url = $"{baseUrl}goform_get_cmd_process?isTest=false&cmd=sms_capacity_info&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        HttpResponseMessage response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        string responseBody = await response.Content.ReadAsStringAsync();
        Console.WriteLine("SMS Capacity Response: " + responseBody);
    }

    /// <summary>
    /// Processes the SMS list retrieved from the router, detecting new messages
    /// and updating the Windows Registry to track changes.
    /// </summary>
    /// <param name="client">An instance of HttpClient for sending HTTP requests.</param>
    static async Task ProcessSmsList(HttpClient client)
    {
        string url = @$"{baseUrl}goform_get_cmd_process?isTest=false&cmd=sms_data_total&page=0&data_per_page=500&mem_store=1&tags=10&order_by=order+by+id+desc";
        HttpResponseMessage response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        string responseBody = await response.Content.ReadAsStringAsync();

        try
        {
            Root root = JsonSerializer.Deserialize<Root>(responseBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            foreach (var message in root.Messages.OrderBy(m => int.Parse(m.Id)))
            {
                Console.WriteLine($"ID: {message.Id}");
                Console.WriteLine($"Number: {message.Number}");
                Console.WriteLine($"Content: {ConvertUTF16(message.Content)}");
                Console.WriteLine($"Tag: {message.Tag}");
                Console.WriteLine($"Date: {message.Date}");
                Console.WriteLine();
            }

            string currentHash = ComputeMD5(string.Join("", root.Messages
                .Where(m => m.Id == "0" || m.Id == "1")
                .Select(m => m.Content + m.Date)));

            string savedHash = ReadRegistryValue(RegistryKeyPath, RegistryValueName);

            if (currentHash != savedHash)
            {
                Console.WriteLine("New messages detected.");
                WriteRegistryValue(RegistryKeyPath, RegistryValueName, currentHash);
            }
        }
        catch (Exception)
        {
            Console.WriteLine("Messages are not available.");
        }
    }

    /// <summary>
    /// Computes the SHA256 hash of the input string.
    /// </summary>
    /// <param name="input">The input string to hash.</param>
    /// <returns>The SHA256 hash as a hexadecimal string.</returns>
    static string ComputeSHA256(string input)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = sha256.ComputeHash(bytes);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToUpper();
    }

    /// <summary>
    /// Encodes a string to Base64 format.
    /// </summary>
    /// <param name="inString">The input string to encode.</param>
    /// <returns>The Base64-encoded string.</returns>
    static string ConvertBase64(string inString)
    {
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(inString));
    }

    /// <summary>
    /// Computes the MD5 hash of the input string.
    /// </summary>
    /// <param name="input">The input string to hash.</param>
    /// <returns>The MD5 hash as a hexadecimal string.</returns>
    static string ComputeMD5(string input)
    {
        using MD5 md5 = MD5.Create();
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        byte[] hashBytes = md5.ComputeHash(bytes);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToUpper();
    }

    /// <summary>
    /// Converts a UTF16-encoded hexadecimal string into a readable string.
    /// </summary>
    /// <param name="hexString">The UTF16-encoded string.</param>
    /// <returns>The decoded string.</returns>
    static string ConvertUTF16(string hexString)
    {
        string strOut = string.Empty;
        for (int i = 2; i < hexString.Length; i += 4)
        {
            strOut += Convert.ToChar(Convert.ToByte(hexString.Substring(i, 2), 16));
        }
        return strOut;
    }

    /// <summary>
    /// Reads a value from the Windows Registry.
    /// </summary>
    /// <param name="keyPath">The registry key path.</param>
    /// <param name="valueName">The name of the registry value.</param>
    /// <returns>The value as a string, or null if not found.</returns>
    static string ReadRegistryValue(string keyPath, string valueName)
    {
        using RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(valueName)?.ToString();
    }

    /// <summary>
    /// Writes a value to the Windows Registry.
    /// </summary>
    /// <param name="keyPath">The registry key path.</param>
    /// <param name="valueName">The name of the registry value.</param>
    /// <param name="value">The value to write.</param>
    static void WriteRegistryValue(string keyPath, string valueName, string value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath);
        key.SetValue(valueName, value);
    }
}

public class Message
{
    public string Id { get; set; }
    public string Number { get; set; }
    public string Content { get; set; }
    public string Tag { get; set; }
    public string Date { get; set; }
    public string DraftGroupId { get; set; }
    public string ReceivedAllConcatSms { get; set; }
    public string ConcatSmsTotal { get; set; }
    public string ConcatSmsReceived { get; set; }
    public string SmsClass { get; set; }
}

public class Root
{
    public List<Message> Messages { get; set; }
}
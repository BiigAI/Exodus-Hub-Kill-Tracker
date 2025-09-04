using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ExodusHub_Kill_Tracker
{
    internal class HTTPClient
    {
        private readonly HttpClient _client;
        private string _apiBaseUrl; // Store the base API URL
        private string _authToken;
        private const string CLIENT_VERSION = "B1";

        // Add public static property to expose version
        public static string Version => CLIENT_VERSION;

        //public HTTPClient(string apiUrl = "http://localhost:3000/api", string authToken = null)
        public HTTPClient(string apiUrl = "https://sc.exoduspmc.org/api", string authToken = null)
        {
            _client = new HttpClient();
            _apiBaseUrl = apiUrl.TrimEnd('/');
            _authToken = authToken;
        }

        public void SetApiUrl(string apiUrl)
        {
            if (string.IsNullOrWhiteSpace(apiUrl))
                throw new ArgumentException("API URL cannot be empty", nameof(apiUrl));

            _apiBaseUrl = apiUrl.TrimEnd('/');
        }

        public void SetAuthToken(string token)
        {
            _authToken = token;
        }

        private void AddAuthHeaders(HttpRequestMessage request)
        {
            if (!string.IsNullOrWhiteSpace(_authToken))
            {
                request.Headers.Remove("x-kill-tracker-token");
                request.Headers.Add("x-kill-tracker-token", _authToken);
            }
            // Optionally, add client version header for future use
            request.Headers.Remove("x-kill-tracker-client-version");
            request.Headers.Add("x-kill-tracker-client-version", CLIENT_VERSION);
        }

        public async Task<bool> SendKillDataAsync(KillData killData)
        {
            try
            {
                string json = JsonSerializer.Serialize(killData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, _apiBaseUrl + "/kills")
                {
                    Content = content
                };
                AddAuthHeaders(request);

                var response = await _client.SendAsync(request);

                return response.IsSuccessStatusCode;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<(bool Success, string Message)> SendKillDataWithDetailsAsync(KillData killData)
        {
            try
            {
                string json = JsonSerializer.Serialize(killData, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, _apiBaseUrl + "/kills")
                {
                    Content = content
                };
                AddAuthHeaders(request);

                var response = await _client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    return (true, "Kill data sent successfully");
                }
                else
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    try
                    {
                        var jsonContent = System.Text.Json.JsonDocument.Parse(responseContent);
                        if (jsonContent.RootElement.TryGetProperty("code", out var codeElement))
                        {
                            string code = codeElement.GetString();
                            switch (code)
                            {
                                case "AUTHENTICATION_REQUIRED":
                                    return (false, "Please log in to the website to get your authentication token.");
                                case "INVALID_TOKEN":
                                    return (false, "Invalid token format or token not found.");
                                case "TOKEN_EXPIRED":
                                    return (false, "Token has expired, please get a new one from the website.");
                                case "TOKEN_IN_USE":
                                    return (false, "Token is already being used by another client.");
                                case "OUTDATED_CLIENT":
                                    return (false, "Client version is outdated, please update the application.");
                            }
                        }
                        if (jsonContent.RootElement.TryGetProperty("message", out var messageElement))
                        {
                            return (false, messageElement.GetString());
                        }
                    }
                    catch
                    {
                        // Ignore JSON parsing errors, fall back to status code
                    }
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        return (false, "Authentication failed. Please check your token.");
                    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        return (false, "Access forbidden. Please check your token and permissions.");
                    return (false, $"Server returned status code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Error sending data: {ex.Message}");
            }
        }

        // Verifies connection to the API by sending a simple GET request
        public async Task<(bool Success, string Message)> VerifyApiConnectionAsync()
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, _apiBaseUrl + "/status");
                AddAuthHeaders(request);

                var response = await _client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    return (true, "Successfully connected to the server API.");
                }
                else
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    try
                    {
                        var jsonContent = System.Text.Json.JsonDocument.Parse(responseContent);

                        // Check for tokenValidation in the response (new server behavior)
                        if (jsonContent.RootElement.TryGetProperty("tokenValidation", out var tokenValidation))
                        {
                            if (tokenValidation.TryGetProperty("valid", out var validElement) && !validElement.GetBoolean())
                            {
                                if (tokenValidation.TryGetProperty("reason", out var reasonElement))
                                {
                                    string reason = reasonElement.GetString();
                                    switch (reason)
                                    {
                                        case "INVALID_TOKEN_FORMAT":
                                            return (false, "Invalid token format. Please get a new token from the website.");
                                        case "TOKEN_NOT_FOUND":
                                            return (false, "Token not found. Please get a new token from the website.");
                                        case "TOKEN_EXPIRED":
                                            return (false, "Token has expired. Please get a new token from the website.");
                                        case "TOKEN_IN_USE_ELSEWHERE":
                                            return (false, "Token is already being used by another client.");
                                        case "OUTDATED_CLIENT":
                                            return (false, "Client version is outdated. Please update the application.");
                                        case "VALIDATION_ERROR":
                                            return (false, "Server error validating token. Please try again later.");
                                        default:
                                            return (false, $"Token validation failed: {reason}");
                                    }
                                }
                                return (false, "Token validation failed.");
                            }
                        }

                        // Fallback to old error handling for backwards compatibility
                        if (jsonContent.RootElement.TryGetProperty("code", out var codeElement))
                        {
                            string code = codeElement.GetString();
                            switch (code)
                            {
                                case "AUTHENTICATION_REQUIRED":
                                    return (false, "Please log in to the website to get your authentication token.");
                                case "INVALID_TOKEN":
                                    return (false, "Invalid token format or token not found.");
                                case "TOKEN_EXPIRED":
                                    return (false, "Token has expired, please get a new one from the website.");
                                case "TOKEN_IN_USE":
                                    return (false, "Token is already being used by another client.");
                                case "OUTDATED_CLIENT":
                                    return (false, "Client version is outdated, please update the application.");
                            }
                        }
                        if (jsonContent.RootElement.TryGetProperty("message", out var messageElement))
                        {
                            return (false, messageElement.GetString());
                        }
                    }
                    catch
                    {
                        // Ignore JSON parsing errors, fall back to status code
                    }
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        return (false, "Authentication failed. Please check your token.");
                    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        return (false, "Access forbidden. Please check your token and permissions.");
                    return (false, $"Server responded with status code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Error connecting to server API: {ex.Message}");
            }
        }
    }
}

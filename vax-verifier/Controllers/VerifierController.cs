using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Identity.Client;
using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using System.Text.Json;
using vax_verifier.Models;
using Microsoft.EntityFrameworkCore;

namespace vax_verifier
{
    [Route("api/[controller]/[action]")]
    public class VerifierController : Controller
    {

        protected readonly AppSettingsModel _appSettings;
        protected IMemoryCache _cache;
        protected readonly ILogger<VerifierController> _log;
        private HttpClient _httpClient;
        private IConfidentialClientApplication _msal;

        public VerifierController(
            IOptions<AppSettingsModel> appSettings, 
            IMemoryCache memoryCache, 
            ILogger<VerifierController> log, 
            IHttpClientFactory httpClientFactory,
            MsalTokenProvider msal)
        {
            _msal = msal._client;
            _appSettings = appSettings.Value;
            _cache = memoryCache;
            _log = log;
            _httpClient = httpClientFactory.CreateClient();
        }

        private PresentationRequest CreatePresentationRequest(string requestId)
        {
            var request = new PresentationRequest
            {
                IncludeQRCode = false, // change to b64 if you would like this included in response
                Callback = new Callback
                {
                    State = requestId,
                    Url = _appSettings.PresentationCallbackUrl
                },
                Authority = _appSettings.VerifierAuthority,
                Registration = new Registration()
                {
                    ClientName = _appSettings.ClientName
                },
                Presentation = new Presentation
                {
                    IncludeReceipt = true,
                    RequestedCredentials = new List<RequestedCredential>()
                    {
                        new RequestedCredential
                        {
                            Type = _appSettings.credType, // Get the credtype from the config
                            Purpose = "Verifying global vaccination status",
                            AcceptedIssuers = new List<string>() { "did:ion:EiAzE6rZmBRPYM2T4bmk5AgqtaDwSRKN9H51AXIlo08eKA:eyJkZWx0YSI6eyJwYXRjaGVzIjpbeyJhY3Rpb24iOiJyZXBsYWNlIiwiZG9jdW1lbnQiOnsicHVibGljS2V5cyI6W3siaWQiOiJzaWdfNzU0Y2VlNWIiLCJwdWJsaWNLZXlKd2siOnsiY3J2Ijoic2VjcDI1NmsxIiwia3R5IjoiRUMiLCJ4IjoicUNVd3gxZDF2ZW5lZ2ppU09OU1U3cHFUbEdPSVZQODc2OHUwSVpSOG8wVSIsInkiOiJpQllLU2VfUVBXUFpMX0FfU294N1E3dW5LMFQwaGxPRDNPLUdYMHQ1d0VBIn0sInB1cnBvc2VzIjpbImF1dGhlbnRpY2F0aW9uIiwiYXNzZXJ0aW9uTWV0aG9kIl0sInR5cGUiOiJFY2RzYVNlY3AyNTZrMVZlcmlmaWNhdGlvbktleTIwMTkifV0sInNlcnZpY2VzIjpbeyJpZCI6ImxpbmtlZGRvbWFpbnMiLCJzZXJ2aWNlRW5kcG9pbnQiOnsib3JpZ2lucyI6WyJodHRwczovL2NocmlzcGFkZ2V0dGxpdmVjb20ub25taWNyb3NvZnQuY29tLyJdfSwidHlwZSI6IkxpbmtlZERvbWFpbnMifV19fV0sInVwZGF0ZUNvbW1pdG1lbnQiOiJFaUEwUEZxQVB5VkdqV19yaVd5Wl9HanZVNWFROFNpSzlJbVBQekFIcy1RZDJBIn0sInN1ZmZpeERhdGEiOnsiZGVsdGFIYXNoIjoiRWlBZTBMY1RKVUF1SGRHS2VhelNCYTJMYjVUbm5mUjFteG1SS1Y2dmhFcG0zdyIsInJlY292ZXJ5Q29tbWl0bWVudCI6IkVpQmlKYzhCXzVpa1JkUWJMLVpUQzBKQ0t1NlM5WWxnZVd5b280bkZ0NHR6SncifX0", "did:ion:EiDqqAHJBihbcWGmnFWfx56SxOine_322th94mp8SjKslw:eyJkZWx0YSI6eyJwYXRjaGVzIjpbeyJhY3Rpb24iOiJyZXBsYWNlIiwiZG9jdW1lbnQiOnsicHVibGljS2V5cyI6W3siaWQiOiJzaWdfMTI0OWFjZmQiLCJwdWJsaWNLZXlKd2siOnsiY3J2Ijoic2VjcDI1NmsxIiwia3R5IjoiRUMiLCJ4IjoiLXQzMFhQTHVTWmZVWU9hOWpTdDlSTG4xSXVWX2l5YXRGZVhmMWNqYmlGVSIsInkiOiJnNmZNUV9aMlVPcEdwN1g5VlVScklDUTg2Z3pKZ0l1WnR3UnJRemY3Y0ZFIn0sInB1cnBvc2VzIjpbImF1dGhlbnRpY2F0aW9uIiwiYXNzZXJ0aW9uTWV0aG9kIl0sInR5cGUiOiJFY2RzYVNlY3AyNTZrMVZlcmlmaWNhdGlvbktleTIwMTkifV0sInNlcnZpY2VzIjpbeyJpZCI6ImxpbmtlZGRvbWFpbnMiLCJzZXJ2aWNlRW5kcG9pbnQiOnsib3JpZ2lucyI6WyJodHRwczovL2RpZC5kZW1vLmFyaW5jby5jb20uYXUvIl19LCJ0eXBlIjoiTGlua2VkRG9tYWlucyJ9XX19XSwidXBkYXRlQ29tbWl0bWVudCI6IkVpRDZUWUdfaGN1WWctZ19Cdzc1ZGFydjVCQUhtQ2JfWDZOVG9Ja19pRWZ5cFEifSwic3VmZml4RGF0YSI6eyJkZWx0YUhhc2giOiJFaUFmWkFHeWFja1Z1U3JtcG91RmxrOENmTXpUYzVkNWE3ZnlwT3VlMmw2OEhBIiwicmVjb3ZlcnlDb21taXRtZW50IjoiRWlBa2VtN1lwV093azZWWjJITkdJLWt2QWhGamtLMHJrNXhJZFllUzU0UnBVQSJ9fQ" } //Fix this later (await _context.AcceptedVerifiableCredentials.ToListAsync()).Select(e => e.CredentialDid).ToList(), //change accepted issuers here, using model data new List<string>() { _appSettings.IssuerAuthority } //
                        }
                    }
                }
            };
            return request;
        }

        [HttpGet("/api/verifier/presentation-request")]
        public async Task<ActionResult> PresentationRequest()
        {
            try
            {
                 _log.LogInformation($"Starting new verification request for credential");
                var t = await _msal.AcquireTokenForClient(new[] { _appSettings.VCServiceScope }).ExecuteAsync();
                if (t.AccessToken == String.Empty)
                {
                    _log.LogError("Failed to acquire accesstoken");
                    return BadRequest(new { error = "no access token", error_description = "Authentication Failed" });
                }
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", t.AccessToken);

                string requestId = Guid.NewGuid().ToString();
                var jsonString = JsonSerializer.Serialize(CreatePresentationRequest(requestId));
                try
                {
                    var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
                    var serviceRequest = await _httpClient.PostAsync(_appSettings.ApiEndpoint, content);
                    var response = await serviceRequest.Content.ReadAsStringAsync();
                    var res = System.Text.Json.JsonSerializer.Deserialize<VcResponse>(response);

                    _log.LogTrace("succesfully called Request API");
                    var statusCode = serviceRequest.StatusCode;

                    if (statusCode == HttpStatusCode.Created)
                    {
                        var cacheData = new CacheObject
                        {
                            Status = "notscanned",
                            Message = "Request ready, please scan with Authenticator",
                            Expiry = res.Expiry.ToString()
                        };
                        _cache.Set(requestId, JsonSerializer.Serialize(cacheData));

                        return new OkObjectResult(res);
                    }
                    else
                    {
                        _log.LogError("Unsuccesfully called Request API");
                        return BadRequest(new { error = "400", error_description = "Something went wrong calling the API: " + response });
                    }
                }
                catch (Exception ex)
                {
                    return BadRequest(new { error = "400", error_description = "Something went wrong calling the API: " + ex.Message });
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = "400", error_description = ex.Message });
            }
        }

        [HttpPost]
        public async Task<ActionResult> PresentationCallback()
        {
            try
            {
                string content = await new StreamReader(this.Request.Body).ReadToEndAsync();

                var presentation = string.IsNullOrEmpty(content)
                    ? null
                    : JsonSerializer.Deserialize<PresentationCallback>(content);

                if(presentation == null)
                {
                    _log.LogError("No presentation response received in body");
                }

                Debug.WriteLine("callback!: " + presentation.RequestId);
                var requestId = presentation.RequestId;

                if (presentation.Code.Equals("request_retrieved", StringComparison.InvariantCultureIgnoreCase))
                {
                    var cacheData = new CacheObject
                    {
                        Status = "request_retrieved",
                        Message = "QR Code is scanned. Please select allow on your device...",
                    };
                    _cache.Set(requestId, JsonSerializer.Serialize(cacheData));
                }

                if (presentation.Code.Equals("presentation_verified", StringComparison.CurrentCultureIgnoreCase))
                {   
                    var cacheData = new CacheObject
                    {
                        Status = "presentation_verified",
                        Message = "Presentation verified",
                        Payload = string.Join(",", presentation.Issuers.First().Type),
                        Subject = presentation.Subject,
                        
                        Name =  $"{presentation.Issuers.First().Claims.FirstName} {presentation.Issuers.First().Claims.LastName}",
                        FirstName =  $"{presentation.Issuers.First().Claims.FirstName}",
                        LastName =  $"{presentation.Issuers.First().Claims.LastName}"
                    };
                    _cache.Set(requestId, JsonSerializer.Serialize(cacheData));
                }

                return new OkResult();
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = "400", error_description = ex.Message });
            }
        }

        [HttpGet("/api/verifier/presentation-response")]
        public ActionResult PresentationResponse(string id)
        {
            try
            {
                //string state = this.Request.Query["id"];
                var state = id;
                if (string.IsNullOrEmpty(state))
                {
                    return BadRequest(new { error = "400", error_description = "Missing argument 'id'" });
                }
                if (_cache.TryGetValue(state, out string buf))
                {
                    var cacheObject = JsonSerializer.Deserialize<CacheObject>(buf);

                    Debug.WriteLine("check if there was a response yet: " + cacheObject.Status);
                    return new ContentResult { ContentType = "application/json", Content = JsonSerializer.Serialize(cacheObject) };
                }

                return new OkResult();
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = "400", error_description = ex.Message });
            }
        }
    }
}

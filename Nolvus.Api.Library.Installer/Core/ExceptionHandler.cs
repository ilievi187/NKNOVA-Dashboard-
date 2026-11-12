using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json;

namespace Vcc.Nolvus.Api.Installer.Core
{
    public class ApiException
    {
        [JsonProperty(PropertyName = "ExceptionMessage")]
        public string ExceptionMessage { get; set; }

        [JsonProperty(PropertyName = "ExceptionType")]
        public string ExceptionType { get; set; }

        [JsonProperty(PropertyName = "StackTrace")]
        public string StackTrace { get; set; }
    }

    public static class ExceptionHandler
    {
        public static void ThrowWebApiException(HttpResponseMessage Response)
        {
            var content = Response.Content.ReadAsStringAsync().Result;
            try
            {
                var ErrorAsJson = JsonConvert.DeserializeObject<ApiException>(content);
                if (ErrorAsJson != null && !string.IsNullOrEmpty(ErrorAsJson.ExceptionMessage))
                {
                    throw new WebApiException(ErrorAsJson.ExceptionMessage, (int)Response.StatusCode, ErrorAsJson);
                }
            }
            catch (WebApiException)
            {
                throw;
            }
            catch
            {
            }

            throw new Exception($"[HTTP {(int)Response.StatusCode} {Response.ReasonPhrase}] {content}");
        }
    }
}

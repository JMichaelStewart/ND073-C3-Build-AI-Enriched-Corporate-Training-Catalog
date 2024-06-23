using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FunctionApp1
{
    public class Function1
    {
        private readonly ILogger<Function1> _logger;
        static readonly string apikey = "5b459e668a6c14dcd8511126e6f5c71e";
        static readonly string springerapiendpoint = "http://api.springernature.com/openaccess/json";

        public Function1(ILogger<Function1> logger)
        {
            _logger = logger;
        }

        #region Class used to deserialize the request
        public class InputRecordData
        {
            public string ArticleName { get; set; }
        }

        public class InputRecord
        {
            public string RecordId { get; set; }
            public InputRecordData Data { get; set; }
        }

        public class WebApiRequest
        {
            public List<InputRecord> Values { get; set; }
        }
        #endregion

        #region Classes used to serialize the response

        private class OutputRecord
        {
            public class OutputRecordData
            {
                public string PublicationName { get; set; } = "";
                public string Publisher { get; set; } = "";
                public string DOI { get; set; } = "";
                public string PublicationDate { get; set; } = "";
            }

            public class OutputRecordMessage
            {
                public string Message { get; set; }
            }

            public string RecordId { get; set; }
            public OutputRecordData Data { get; set; }
            public List<OutputRecordMessage> Errors { get; set; }
            public List<OutputRecordMessage> Warnings { get; set; }
        }

        private class WebApiResponse
        {
            public List<OutputRecord> Values { get; set; }
        }
        #endregion

        [Function("SpringerLookup")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequest req)
        {
            try
            {
                _logger.LogInformation("C# HTTP trigger function processed a request.");
                var response = new WebApiResponse
                {
                    Values = new List<OutputRecord>()
                };
                _logger.LogInformation("Read request body");
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                _logger.LogInformation($"{requestBody}");
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var data = JsonSerializer.Deserialize<WebApiRequest>(requestBody, options);
                _logger.LogInformation("Parsed body into WebApiRequest object");

                // Do some schema validation
                if (data == null)
                {
                    return new BadRequestObjectResult("The request schema does not match expected schema.");
                }
                if (data.Values == null)
                {
                    return new BadRequestObjectResult("The request schema does not match expected schema. Could not find values array.");
                }

                // Calculate the response for each value.
                foreach (var record in data.Values)
                {
                    if (record == null || record.RecordId == null) continue;

                    OutputRecord responseRecord = new OutputRecord
                    {
                        RecordId = record.RecordId
                    };

                    try
                    {
                        responseRecord.Data = GetEntityMetadata(record.Data.ArticleName).Result;
                    }
                    catch (Exception e)
                    {
                        // Something bad happened, log the issue.
                        var error = new OutputRecord.OutputRecordMessage
                        {
                            Message = e.Message
                        };

                        responseRecord.Errors = new List<OutputRecord.OutputRecordMessage>
                    {
                        error
                    };
                    }
                    finally
                    {
                        response.Values.Add(responseRecord);
                    }
                }

                return (ActionResult)new OkObjectResult(response);

            }
            catch (Exception e)
            {
                _logger.LogError($"Error: {e}");
                return (ActionResult) new BadRequestObjectResult("Unable to process request");
            }

        }

        #region Methods to call the Springer API


        private async static Task<OutputRecord.OutputRecordData> GetEntityMetadata(string title)
        {
            var uri = springerapiendpoint + "?q=title:\"" + title + "\"&api_key=" + apikey;
            var result = new OutputRecord.OutputRecordData();

            using (var client = new HttpClient())
            using (var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(uri)
            })
            {
                HttpResponseMessage response = await client.SendAsync(request);
                string responseBody = await response?.Content?.ReadAsStringAsync();
                JsonDocument springerresults = JsonDocument.Parse(responseBody);

                if (springerresults.RootElement.TryGetProperty("records", out JsonElement recordsElement) && recordsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement recordElement in recordsElement.EnumerateArray())
                    {
                        if (recordElement.TryGetProperty("doi", out JsonElement recordIdElement))
                        {
                            result.DOI = recordIdElement.GetString();
                        }
                        if (recordElement.TryGetProperty("publicationDate", out JsonElement publicationDate))
                        {
                            result.PublicationDate = publicationDate.GetString();
                        }
                        if (recordElement.TryGetProperty("publicationName", out JsonElement publicationName))
                        {
                            result.PublicationName = publicationName.GetString();
                        }
                        if (recordElement.TryGetProperty("publisher", out JsonElement publisher))
                        {
                            result.Publisher = publisher.GetString();
                        }
                    }
                }
            }

            return result;
        }


        #endregion
    }
}

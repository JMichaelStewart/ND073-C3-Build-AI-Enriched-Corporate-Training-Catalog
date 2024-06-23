using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Functions.Worker;
using System.Text.Json;

namespace Udacity.springerlookupdemo
{
    
    public static class SpringerLookup
    {
        
        /* 

          This is where you will configure your Spring API Key
          
        */
        static readonly string apikey = "5b459e668a6c14dcd8511126e6f5c71e";
        static readonly string springerapiendpoint = "http://api.springernature.com/openaccess/json";

        #region Class used to deserialize the request
        private class InputRecord
        {
            public class InputRecordData
            {
                public string ArticleName { get; set; }
            }

            public string RecordId { get; set; }
            public InputRecordData Data { get; set; }
        }

        private class WebApiRequest
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
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req)
        {
            // log.LogInformation("Entity Search function: C# HTTP trigger function processed a request.");

            var response = new WebApiResponse
            {
                Values = new List<OutputRecord>()
            };

            string requestBody = new StreamReader(req.Body).ReadToEnd();
            var data = JsonSerializer.Deserialize<WebApiRequest>(requestBody);

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

        #region Methods to call the Springer API
        
        
        private async static Task<OutputRecord.OutputRecordData> GetEntityMetadata(string title)
        {
            var uri = springerapiendpoint + "?q=title:\"" + title + "\"&api_key=" + apikey;
            var result = new OutputRecord.OutputRecordData();

            using (var client = new HttpClient())
            using (var request = new HttpRequestMessage {
                Method = HttpMethod.Get,
                RequestUri = new Uri(uri)
            })
            {
                HttpResponseMessage response = await client.SendAsync(request);
                string responseBody = await response?.Content?.ReadAsStringAsync();
                JsonDocument springerresults = JsonDocument.Parse(responseBody);
                
                foreach(JsonProperty t in springerresults.RootElement.EnumerateObject()){
                    switch(t.Name)
                    {
                        case "doi":
                            result.DOI = t.Value.ToString();
                            break;
                        case "publicationDate":
                            result.PublicationDate = t.Value.ToString();
                            break;
                        case "publicationName":
                            result.PublicationName = t.Value.ToString(); 
                            break;
                        case "publisher":
                            result.Publisher = t.Value.ToString();
                            break;
                    }
                }
            }

            return result;
        }

        
        #endregion
    }
}

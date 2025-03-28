using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Web;

namespace My.Function
{
    public class WeatherFunction
    {
        private const string WEATHER_API_KEY = "[OPENWEATHERMAP_API_HERE]";
        private static readonly string[] VALID_UNITS = { "metric", "imperial" };
        private readonly ILogger<WeatherFunction> _logger;
        private readonly HttpClient _httpClient;

        public WeatherFunction(ILogger<WeatherFunction> logger, HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        [Function("weather")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestData req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            // Extract query parameters
            var queryParams = HttpUtility.ParseQueryString(req.Url.Query);
            string cityId = queryParams["cityId"];
            string units = queryParams["units"];

            // Validate city ID
            if (string.IsNullOrEmpty(cityId) || !int.TryParse(cityId, out _))
            {
                var badRequestResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid cityId. Make sure you are sending it to the queryString and that it's a valid integer");
                return badRequestResponse;
            }

            // Validate units
            if (!string.IsNullOrEmpty(units) && !Array.Exists(VALID_UNITS, u => u == units))
            {
                var badRequestResponse = req.CreateResponse(System.Net.HttpStatusCode.BadRequest);
                await badRequestResponse.WriteStringAsync("Invalid unit type. You may only use imperial or metric");
                return badRequestResponse;
            }

            // Prepare query string dictionary
            var queryStringDictionary = new Dictionary<string, string>
            {
                { "id", cityId },
                { "APPID", WEATHER_API_KEY },
                { "units", units ?? "metric" }
            };

            try
            {
                // Get weather data
                var output = await DoGetRequest(GetApiUrl(queryStringDictionary));
                
                // Create response
                var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
                await response.WriteAsJsonAsync(output);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching weather data");
                var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync("An error occurred while fetching weather data");
                return errorResponse;
            }
        }


        private string GetApiUrl(Dictionary<string, string> queryString)
        {
            string apiBaseUrl = "http://api.openweathermap.org/data";
            string apiVersion = "2.5";
            string apiMethod = "weather";

            var apiUrlBuilder = new System.Text.StringBuilder($"{apiBaseUrl}/{apiVersion}/{apiMethod}?");

            foreach (var queryElement in queryString)
            {
                apiUrlBuilder.Append($"{queryElement.Key}={queryElement.Value}&");
            }

            return apiUrlBuilder.ToString().TrimEnd('&');
        }

        private async Task<string> DoGetRequest(string apiUrl)
        {
            var response = await _httpClient.GetAsync(apiUrl);
            var jsonContent = await response.Content.ReadAsStringAsync();

            if ((int)response.StatusCode != 200)
            {
                var errInfo = new JsonOutput("ERROR", (int)response.StatusCode, response.ReasonPhrase);
                return JsonConvert.SerializeObject(errInfo);
            }
            else
            {
                var weatherResponse = JsonConvert.DeserializeObject<WeatherAPIResponse>(jsonContent);
                var weatherEntity = new WeatherEntity(weatherResponse);

                var outInfo = new JsonOutput("OK", (int)response.StatusCode, weatherEntity);
                return JsonConvert.SerializeObject(outInfo);
            }
        }

        // Nested classes
        private class JsonOutput
        {
            public string Type { get; }
            public int Status { get; }
            public object Response { get; }

            public JsonOutput(string type, int status, object response)
            {
                Type = type;
                Status = status;
                Response = response;
            }
        }

        // Nested classes for WeatherEntity and WeatherAPIResponse
        private class WeatherEntity
        {
            public List<WeatherInfo> WeatherList { get; set; }
            public double Temperature { get; set; }
            public double Humidity { get; set; }
            public double Pressure { get; set; }
            public double WindSpeed { get; set; }
            public string WindDirection { get; set; }
            public long Sunrise { get; set; }
            public long Sunset { get; set; }

            public class WeatherInfo
            {
                public string Name { get; set; }
                public string Description { get; set; }
                public string Icon { get; set; }
            }

            public WeatherEntity(WeatherAPIResponse response)
            {
                WeatherList = response.Weather.Select(w => new WeatherInfo
                {
                    Name = w.Main,
                    Description = w.Description,
                    Icon = w.Icon
                }).ToList();

                Temperature = response.Main.Temp;
                Humidity = response.Main.Humidity;
                Pressure = response.Main.Pressure;
                WindSpeed = response.Wind.Speed;
                WindDirection = GetWindDirection(response.Wind.Deg);
                Sunrise = response.Sys.Sunrise;
                Sunset = response.Sys.Sunset;
            }

            private string GetWindDirection(double deg)
            {
                string[] cardinals = {
                    "N", "NNE", "NE", "ENE", 
                    "E", "ESE", "SE", "SSE", 
                    "S", "SSW", "SW", "WSW", 
                    "W", "WNW", "NW", "NNW", "N"
                };

                return cardinals[(int)Math.Round(((double)deg * 10 % 3600) / 225)];
            }
        }

        private class WeatherAPIResponse
        {
            public class WeatherItem
            {
                public int Id { get; set; }
                public string Main { get; set; }
                public string Description { get; set; }
                public string Icon { get; set; }
            }

            public class MainData
            {
                public double Temp { get; set; }
                public double Pressure { get; set; }
                public double Humidity { get; set; }
            }

            public class WindData
            {
                public double Speed { get; set; }
                public double Deg { get; set; }
            }

            public class SysData
            {
                public long Sunrise { get; set; }
                public long Sunset { get; set; }
            }

            public List<WeatherItem> Weather { get; set; }
            public MainData Main { get; set; }
            public WindData Wind { get; set; }
            public SysData Sys { get; set; }
        }
    }
}
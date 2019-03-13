using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Newtonsoft.Json;
using Microsoft.Azure.CognitiveServices.Language.TextAnalytics;
using Microsoft.Azure.CognitiveServices.Language.TextAnalytics.Models;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.Azure.CognitiveServices.ContentModerator;

namespace ChatTextToSpeech
{
    public static class TextToSpeech
    {
        [FunctionName("TextToSpeech")]
        [return: EventHub(Constants.EventHubReceiver, Connection = "EVENTHUB_CONNECTION_STRING")]
        public static async Task<string> Run([EventHubTrigger(Constants.EventHubSender, Connection = "EVENTHUB_CONNECTION_STRING")] EventData[] events, ILogger log)
        {
            var exceptions = new List<Exception>();

            foreach (EventData eventData in events)
            {
                try
                {
                    string messageBody = Encoding.UTF8.GetString(eventData.Body.Array, eventData.Body.Offset, eventData.Body.Count);

                    Output output = new Output();

                    string CSSubscriptionKey = Environment.GetEnvironmentVariable("SPEECH_KEY");
                    string host = Constants.AzureTextToSpeechURL;
                    string accessToken;

                    if (messageBody != null)
                    {
                        // Add your subscription key here
                        // If your resource isn't in WEST US, change the endpoint
                        Authentication auth = new Authentication("https://westus.api.cognitive.microsoft.com/sts/v1.0/issueToken", CSSubscriptionKey);
                        try
                        {
                            accessToken = await auth.FetchTokenAsync().ConfigureAwait(false);
                            log.LogInformation("Successfully obtained an access token");
                        }
                        catch (Exception ex)
                        {
                            log.LogInformation("Failed to obtain an access token");
                            return null;
                        }

                        using (var client = new HttpClient())
                        {
                            using (var request = new HttpRequestMessage())
                            {
                                string voice;
                                string lang;

                                voice = GetVoice(messageBody);

                                if (voice == null)
                                {
                                    voice = "en-US, ZiraRUS";
                                    lang = "en-US";
                                }
                                else
                                {
                                    String[] substrings = voice.Split(",");
                                    lang = substrings[0];
                                }
                                string body = @"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='" + lang + "'><voice name='Microsoft Server Speech Text to Speech Voice (" + voice + ")'>" + messageBody + "</voice></speak>";

                                // Set the HTTP method
                                request.Method = HttpMethod.Post;
                                // Construct the URI
                                request.RequestUri = new Uri(host);
                                // Set the content type header
                                request.Content = new StringContent(body, Encoding.UTF8, "application/ssml+xml");
                                // Set additional header, such as Authorization and User-Agent
                                request.Headers.Add("Authorization", "Bearer " + accessToken);
                                request.Headers.Add("Connection", "Keep-Alive");
                                // Update your resource name
                                request.Headers.Add("User-Agent", "SS-IGCE");
                                request.Headers.Add("X-Microsoft-OutputFormat", "riff-24khz-16bit-mono-pcm");
                                // Create a request
                                log.LogInformation("Calling the TTS service. Please wait...");
                                using (var response = await client.SendAsync(request).ConfigureAwait(false))
                                {
                                    response.EnsureSuccessStatusCode();

                                    // Asynchronously read the response
                                    using (Stream dataStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                                    {
                                        log.LogInformation("The speech file is being saved...");

                                        string name = Guid.NewGuid().ToString("n");

                                        await CreateBlob(name + ".wav", dataStream, log);

                                        log.LogInformation($"The speech file is saved: {name}.wav");

                                        output.SpeechStoragePointer = name;
                                        output.OriginalString = messageBody;

                                        var outputJson = JsonConvert.SerializeObject(output);

                                        return outputJson;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    // We need to keep processing the rest of the batch - capture this exception and continue.
                    // Also, consider capturing details of the message that failed processing so it can be processed again later.
                    exceptions.Add(e);
                }
            }

            // Once processing of the batch is complete, if any messages in the batch failed processing throw an exception so that there is a record of the failure.

            if (exceptions.Count > 1)
                throw new AggregateException(exceptions);

            if (exceptions.Count == 1)
                throw exceptions.Single();

            return null;
        }

        private async static Task CreateBlob(string name, Stream data, ILogger log)
        {
            CloudStorageAccount storageAccount;
            CloudBlobClient blobClient;
            CloudBlobContainer blobContainer;
            CloudBlockBlob blob;

            string connectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage"); // DefaultEndpointsProtocol=https;AccountName=YOURSTORAGEACCOUNTNAME;AccountKey=YOURSTORAGESUBSCRIPTIONKEY

            storageAccount = CloudStorageAccount.Parse(connectionString);

            blobClient = storageAccount.CreateCloudBlobClient();

            blobContainer = blobClient.GetContainerReference(Constants.StorageSpeechFilesContainer);

            await blobContainer.CreateIfNotExistsAsync();

            blob = blobContainer.GetBlockBlobReference(name);
            blob.Properties.ContentType = "audio/wav";

            await blob.UploadFromStreamAsync(data);
        }

        private static string GetVoice(string input)
        {
            // Full list of languages and voices here: https://docs.microsoft.com/azure/cognitive-services/speech-service/language-support
            // Suggestion: get the preferred language and voice from your user
            Dictionary<string, string> voicesDictionary = new Dictionary<string, string>
            {
                { "it", "it-IT, it-IT, Cosimo, Apollo" },
                { "de", "de-DE, Hedda" },
                { "el", "el-GR, Stefanos" },
                { "es", "es-ES, Pablo, Apollo" },
                { "fr", "fr-FR, fr-FR, Julie, Apollo" },
                { "nl", "da-DK, HelleRUS" },
                { "pt", "pt-PT, HeliaRUS" },
                { "ru", "ru-RU, ru-RU, Pavel, Apollo" },
                { "sv", "sv-SE, HedvigRUS" },
                { "ko", "ko-KR, HeamiRUS" },
                { "zh", "zh-CN, Kangkang, Apollo" },
                { "ja", "ja-JP, Ichiro, Apollo" },
            };

            // Your Text Analytics subscription key.
            string CSSubscriptionKey = Environment.GetEnvironmentVariable("TEXTANALYTICS_KEY");

            // Create a TA client.
            ITextAnalyticsClient client = new TextAnalyticsClient(new ApiKeyServiceClientCredentials(CSSubscriptionKey));
            client.Endpoint = Constants.AzureBaseURLWithRegion;

            var resultLanguage = client.DetectLanguageAsync(new BatchInput(new List<Input>() { new Input("1", input) })).Result;
            var detectedLanguage = resultLanguage.Documents[0].DetectedLanguages[0];

            if (detectedLanguage != null)
            {
                string lang = detectedLanguage.Iso6391Name;

                if (voicesDictionary.ContainsKey(lang))
                {
                    return (voicesDictionary[lang]);
                }
            }

            return null;
        }

        public class Authentication
        {
            private string subscriptionKey;
            private string tokenFetchUri;

            public Authentication(string tokenFetchUri, string subscriptionKey)
            {
                if (string.IsNullOrWhiteSpace(tokenFetchUri))
                {
                    throw new ArgumentNullException(nameof(tokenFetchUri));
                }
                if (string.IsNullOrWhiteSpace(subscriptionKey))
                {
                    throw new ArgumentNullException(nameof(subscriptionKey));
                }
                this.tokenFetchUri = tokenFetchUri;
                this.subscriptionKey = subscriptionKey;
            }

            public async Task<string> FetchTokenAsync()
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", this.subscriptionKey);
                    UriBuilder uriBuilder = new UriBuilder(this.tokenFetchUri);

                    var result = await client.PostAsync(uriBuilder.Uri.AbsoluteUri, null).ConfigureAwait(false);
                    return await result.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }

        class Output
        {
            public string SpeechStoragePointer { get; set; }

            public string OriginalString { get; set; }
        }
    }
}

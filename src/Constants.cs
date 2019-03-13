using System;
using System.Collections.Generic;
using System.Text;

namespace ChatTextToSpeech
{
    class Constants
    {
        // Event Hubs
        public const string EventHubSender = "eh-chattts-sender";
        public const string EventHubReceiver = "eh-chattts-receiver";

        // Storage
        public const string StorageSpeechFilesContainer = "speechfiles";

        // Cognitive service
        public const string AzureBaseURLWithRegion = "https://westus.api.cognitive.microsoft.com";
        public const string AzureTextToSpeechURL = "https://westus.tts.speech.microsoft.com/cognitiveservices/v1";
    }
}

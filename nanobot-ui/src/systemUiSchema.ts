

const systemUiSchema = {
    type: "VerticalLayout",
    elements: [
        {
            type: "Group",
            label: "General",
            elements: [      
                { type: "Control", scope: "#/properties/OpenAiModelId" },
                { type: "Control", scope: "#/properties/OpenAiApiKey" },
                { type: "Control", scope: "#/properties/GoogleApiKey" },
                { type: "Control", scope: "#/properties/GoogleSearchEngineId" },      
                { type: "Control", scope: "#/properties/ChatHistoryTimeToLiveMinutes" }
            ]
      },
      {
        type: "Group",
        label: "Wake Word Engine",
        elements: [      
            { type: "Control", scope: "#/properties/WakeWordSilenceSampleAmplitudeThreshold" }
        ]
      },           
      {
        type: "Group",
        label: "Speech Engine",
        elements: [    
          { type: "Control", scope: "#/properties/PlaybackVolume" },  
          { type: "Control", scope: "#/properties/TextToSpeechServiceProvider" },
          { type: "Control", scope: "#/properties/AzureSpeechServiceKey" },
          { type: "Control", scope: "#/properties/AzureSpeechServiceRegion" }         
        ]
      }
    ]
  };

export default systemUiSchema; 
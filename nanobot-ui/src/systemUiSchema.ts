

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
                { type: "Control", scope: "#/properties/ChatHistoryTimeToLiveMinutes" },
                { type: "Control", scope: "#/properties/UserPluginPath" },
                { type: "Control", scope: "#/properties/PowerConfS330DriverEnabled" },      
            ]
      },            
      {
        type: "Group",
        label: "Voice Service",
        elements: [      
          { type: "Control", scope: "#/properties/VoiceService/properties/TextToSpeechServiceProvider" },
          { type: "Control", scope: "#/properties/VoiceService/properties/AzureSpeechServiceKey" },
          { type: "Control", scope: "#/properties/VoiceService/properties/AzureSpeechServiceRegion" }
        ]
      }
    ]
  };

export default systemUiSchema; 
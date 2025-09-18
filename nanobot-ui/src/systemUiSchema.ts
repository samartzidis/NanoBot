

const systemUiSchema = {
    type: "VerticalLayout",
    elements: [
      { type: "Control", scope: "#/properties/OpenAiModelId" },
      { type: "Control", scope: "#/properties/OpenAiApiKey" },
      { type: "Control", scope: "#/properties/GoogleApiKey" },
      { type: "Control", scope: "#/properties/GoogleSearchEngineId" },
      { type: "Control", scope: "#/properties/PowerConfS330DriverEnabled" },
      { type: "Control", scope: "#/properties/ChatHistoryTimeToLiveMinutes" },
      {
        type: "Group",
        label: "Voice Service",
        elements: [
          { type: "Control", scope: "#/properties/VoiceService/properties/SilenceSampleAmplitudeThreshold" },
          { type: "Control", scope: "#/properties/VoiceService/properties/SilenceSampleCountThreshold" },
          { type: "Control", scope: "#/properties/VoiceService/properties/MaxRecordingDurationSeconds" },
          { type: "Control", scope: "#/properties/VoiceService/properties/StopRecordingSilenceSeconds" },
          
          { type: "Control", scope: "#/properties/VoiceService/properties/TextToSpeechServiceProvider" },
          { type: "Control", scope: "#/properties/VoiceService/properties/AzureSpeechServiceKey" },
          { type: "Control", scope: "#/properties/VoiceService/properties/AzureSpeechServiceRegion" }
        ]
      },
    ]
  };

export default systemUiSchema; 
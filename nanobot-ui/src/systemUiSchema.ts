

const systemUiSchema = {
    type: "VerticalLayout",
    elements: [
        {
            type: "Group",
            label: "General",
            elements: [                      
                { type: "Control", scope: "#/properties/OpenAiApiKey" },
                { type: "Control", scope: "#/properties/OpenAiModel" },
                { type: "Control", scope: "#/properties/SessionTimeoutMinutes" },
                { type: "Control", scope: "#/properties/ConversationInactivityTimeoutSeconds" },
                { type: "Control", scope: "#/properties/MemoryServiceMaxMemories" },                
                { type: "Control", scope: "#/properties/PlaybackVolume" },
                { type: "Control", scope: "#/properties/WakeWordSilenceSampleAmplitudeThreshold" },
                { type: "Control", scope: "#/properties/S330Enabled" },
            ]
      }
    ]
  };

export default systemUiSchema; 
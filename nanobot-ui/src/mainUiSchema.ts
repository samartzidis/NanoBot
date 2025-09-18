const mainUiSchema = {
    type: "VerticalLayout",
    elements: [      
      //{ type: "Control", scope: "#/properties/Instructions", options: { "multi": true } },
      {
        type: "Control",
        scope: "#/properties/Agents",
        options: {
          detail: {
            type: "VerticalLayout",
            elements: [
              { "type": "Control", "scope": "#/properties/Disabled" },
              { "type": "Control", "scope": "#/properties/Name" },              
              { "type": "Control", "scope": "#/properties/Instructions", options: { "multi": true } },
              { "type": "Control", "scope": "#/properties/Temperature" },
              { "type": "Control", "scope": "#/properties/MaxTokens" },
              { "type": "Control", "scope": "#/properties/TopP" },
              { "type": "Control", "scope": "#/properties/MaxHistory" },
              { "type": "Control", "scope": "#/properties/WakeWord" },
              { "type": "Control", "scope": "#/properties/WakeWordThreshold" },
              { "type": "Control", "scope": "#/properties/WakeWordTriggerLevel" },
              { "type": "Control", "scope": "#/properties/StopWord" },
              { "type": "Control", "scope": "#/properties/SpeechSynthesisVoiceName" },
              { "type": "Control", "scope": "#/properties/TimePluginEnabled" },
              { "type": "Control", "scope": "#/properties/GooglePluginEnabled" },
              { "type": "Control", "scope": "#/properties/MemoryPluginEnabled" }
            ]
          }
        }
      }
    ]
  };

  export default mainUiSchema;
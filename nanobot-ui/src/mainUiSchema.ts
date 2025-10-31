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
              { "type": "Control", "scope": "#/properties/MaxHistory" },
              { "type": "Control", "scope": "#/properties/WakeWord" },
              { "type": "Control", "scope": "#/properties/WakeWordThreshold" },
              { "type": "Control", "scope": "#/properties/WakeWordTriggerLevel" },
              { "type": "Control", "scope": "#/properties/SpeechSynthesisVoiceName" },

              { "type": "Control", "scope": "#/properties/MemoryPluginEnabled" },
              { "type": "Control", "scope": "#/properties/DateTimePluginEnabled" },
              { "type": "Control", "scope": "#/properties/GeoIpPluginEnabled" },
              { "type": "Control", "scope": "#/properties/WeatherPluginEnabled" },
              { "type": "Control", "scope": "#/properties/CalculatorPluginEnabled" },
              { "type": "Control", "scope": "#/properties/GooglePluginEnabled" },
              { "type": "Control", "scope": "#/properties/UserPluginsEnabled" }
            ]
          }
        }
      }
    ]
  };

  export default mainUiSchema;